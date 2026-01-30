using System.Data;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;

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

    [Params(100, 1000)] public int RowCount;

    [GlobalSetup]
    public void Setup()
    {
        // Setup pengdows.crud optimized path
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<TestEntity>();
        _context = new DatabaseContext("Data Source=:memory:", factory, _typeMap);
        _helper = new TableGateway<TestEntity, int>(_context);

        // Setup for pure reflection path
        _properties = typeof(TestEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
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
    public List<TestEntity> PengdowsCrud_OptimizedMapping()
    {
        // Create new reader for this iteration
        using var reader = new TestTrackedReader(GenerateTestData());

        var results = new List<TestEntity>();

        // This uses pengdows.crud's optimized path:
        // - Plan built once (column ordinals cached)
        // - Compiled property setters (no reflection per row)
        // - Type extractors cached
        while (reader.Read())
        {
            var entity = _helper.MapReaderToObject(reader);
            results.Add(entity);
        }

        return results;
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