using System.Data;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Dapper;
using DuckDB.NET.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using pengdows.crud.connection;

namespace CrudBenchmarks;

/// <summary>
/// Benchmark comparing pengdows.crud's optimized reader mapping (compiled property setters + plan caching)
/// against pure reflection-based mapping (no caching, no compiled delegates).
///
/// This backs up the performance claims in PENGDOWS_CRUD_OVERVIEW.md section "Compiled Property Setters".
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class ReaderMappingBenchmark
{
    private TableGateway<TestEntity, int> _helper = null!;
    private TypeMapRegistry _typeMap = null!;
    private DatabaseContext _context = null!;
    private PropertyInfo[] _properties = null!;
    private DuckDBConnection _dapperConnection = null!;
    private string _connectionString = null!;

    [Params(100, 1000)] public int RowCount;

    [GlobalSetup]
    public void Setup()
    {
        // Use shared connection string for both Dapper and Pengdows (apples-to-apples)
        // We need a file-backed database so both can share the same data
        _connectionString = "Data Source=benchmark_test.duckdb";

        // Setup Dapper connection
        _dapperConnection = new DuckDBConnection(_connectionString);
        _dapperConnection.Open();

        // Setup pengdows.crud with DuckDB factory and SingleConnection mode (keeps connection open)
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<TestEntity>();
        _context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = _connectionString,
                DbMode = DbMode.SingleConnection
            },
            DuckDBClientFactory.Instance
        );
        _helper = new TableGateway<TestEntity, int>(_context);

        // Verify the actual mode being used
        Console.WriteLine($"[DIAGNOSTIC] Requested: DbMode.SingleConnection, Actual: {_context.ConnectionMode}");

        // Setup for pure reflection path (using FakeDb to show the overhead)
        _properties = typeof(TestEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Create table (drop if exists for clean slate)
        _dapperConnection.Execute("DROP TABLE IF EXISTS test_entities");

        var createTableSql = @"
            CREATE TABLE test_entities (
                id INTEGER PRIMARY KEY,
                name VARCHAR,
                email VARCHAR,
                age INTEGER,
                salary DOUBLE,
                is_active BOOLEAN,
                created_at TIMESTAMP,
                score DOUBLE
            )
        ";

        _dapperConnection.Execute(createTableSql);

        // Pre-populate with max row count (we'll LIMIT in the query)
        var maxRows = 1000; // Maximum RowCount parameter
        var testData = Enumerable.Range(1, maxRows).Select(i => new
        {
            id = i,
            name = $"Entity{i}",
            email = $"user{i}@example.com",
            age = 20 + i % 50,
            salary = 50000.0 + i * 100,
            is_active = i % 2 == 0,
            created_at = DateTime.Now.AddDays(-i),
            score = i * 1.5
        });

        // DuckDB prefers batch inserts - build VALUES list
        var values = testData.Select(row =>
            $"({row.id}, '{row.name}', '{row.email}', {row.age}, {row.salary}, " +
            $"{(row.is_active ? "true" : "false")}, '{row.created_at:yyyy-MM-dd HH:mm:ss}', {row.score})");

        var insertSql = $@"
            INSERT INTO test_entities (id, name, email, age, salary, is_active, created_at, score)
            VALUES {string.Join(", ", values)}
        ";

        _dapperConnection.Execute(insertSql);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dapperConnection?.Dispose();
        _context?.Dispose();

        // Clean up the file-based database
        if (File.Exists("benchmark_test.duckdb"))
        {
            File.Delete("benchmark_test.duckdb");
        }
        // DuckDB also creates a .wal file
        if (File.Exists("benchmark_test.duckdb.wal"))
        {
            File.Delete("benchmark_test.duckdb.wal");
        }
    }

    private IEnumerable<Dictionary<string, object>> GenerateTestData()
    {
        return Enumerable.Range(1, RowCount).Select(i => new Dictionary<string, object>
        {
            ["id"] = i,
            ["name"] = $"Entity{i}",
            ["email"] = $"user{i}@example.com",
            ["age"] = 20 + i % 50,
            ["salary"] = 50000.0m + i * 100,
            ["is_active"] = i % 2 == 0,
            ["created_at"] = DateTime.Now.AddDays(-i),
            ["score"] = i * 1.5
        });
    }

    [Benchmark(Baseline = true)]
    public async Task<List<TestEntity>> PengdowsCrud_OptimizedMapping()
    {
        // Query from SQLite (apples-to-apples with Dapper)
        // This uses pengdows.crud's optimized path:
        // - Hybrid plan: compiled expressions for direct columns + delegates for coercion
        // - Plan built once (column ordinals cached)
        // - Type extractors cached
        var sql = $"SELECT id, name, email, age, salary, is_active, created_at, score FROM test_entities LIMIT {RowCount}";
        var container = _context.CreateSqlContainer(sql);
        return await _helper.LoadListAsync(container);
    }

    [Benchmark]
    public List<TestEntity> PureReflection_NoOptimization()
    {
        // Create new reader for this iteration
        using var reader = new fakeDbDataReader(GenerateTestData().ToArray());

        var results = new List<TestEntity>();

        // Pure reflection approach - what you'd write without optimization:
        // - GetOrdinal called for every column on every row
        // - PropertyInfo.SetValue (slow reflection) on every row
        // - No caching, no compiled delegates
        while (reader.Read())
        {
            var entity = new TestEntity();

            // Manually map each property using reflection (slow path)
            foreach (var prop in _properties)
            {
                var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
                if (columnAttr == null)
                {
                    continue;
                }

                try
                {
                    // GetOrdinal lookup on every row (not cached)
                    var ordinal = reader.GetOrdinal(columnAttr.Name);

                    if (!reader.IsDBNull(ordinal))
                    {
                        var value = reader.GetValue(ordinal);

                        // Convert.ChangeType for every value (no type extractors)
                        if (value != null && value.GetType() != prop.PropertyType)
                        {
                            value = Convert.ChangeType(value, prop.PropertyType);
                        }

                        // Reflection-based property setter (slow!)
                        prop.SetValue(entity, value);
                    }
                }
                catch
                {
                    // Ignore mapping errors
                }
            }

            results.Add(entity);
        }

        return results;
    }

    [Benchmark]
    public List<TestEntity> Dapper_OptimizedMapping()
    {
        // Dapper's optimized path:
        // - Compiled IL emit (no reflection on hot path)
        // - Single compiled expression for entire entity
        // - Zero delegate calls per column (all inlined)
        // - Plan cached by query string + type
        var sql = $"SELECT id, name, email, age, salary, is_active, created_at, score FROM test_entities LIMIT {RowCount}";
        return _dapperConnection.Query<TestEntity>(sql).AsList();
    }

    [Table("test_entities")]
    public class TestEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string? Name { get; set; }

        [Column("email", DbType.String)] public string? Email { get; set; }

        [Column("age", DbType.Int32)] public int Age { get; set; }

        [Column("salary", DbType.Decimal)] public decimal Salary { get; set; }

        [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }

        [Column("score", DbType.Double)] public double Score { get; set; }
    }

    /// <summary>
    /// Simple wrapper to make fakeDbDataReader work as ITrackedReader for pengdows.crud API
    /// </summary>
    private class TestTrackedReader : fakeDbDataReader, ITrackedReader
    {
        public TestTrackedReader(IEnumerable<Dictionary<string, object>> rows) : base(rows.ToArray())
        {
        }

        public new Task<bool> ReadAsync()
        {
            return base.ReadAsync(CancellationToken.None);
        }

        public new Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return base.ReadAsync(cancellationToken);
        }
    }
}