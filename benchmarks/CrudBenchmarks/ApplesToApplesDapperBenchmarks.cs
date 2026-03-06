using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// Apples-to-apples comparison between Dapper and pengdows.crud:
/// - Same SQLite in-memory database
/// - New connection per operation (factory -> connection string -> open)
/// - Container built once via BuildRetrieve, reused across iterations via SetParameterValue
/// - Per-iteration cost is parameter update + execution + mapping
///
/// Two pengdows.crud variants:
///   ProductionStandard — connection-per-op (governor + open/close overhead, same as Dapper)
///   PureMapping        — SingleConnection mode (shared persistent connection; mapping engine only)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ApplesToApplesDapperBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private SqliteConnection _sentinel = null!;

    private DatabaseContext _pengdowsContext = null!;
    private DatabaseContext _pengdowsSingleContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private TableGateway<BenchEntity, int> _singleGateway = null!;
    private TypeMapRegistry _typeMap = null!;

    private DbProviderFactory _factory = null!;

    private string _connectionString = string.Empty;

    // Reusable ISqlContainer fields — built once via BuildRetrieve; loop calls SetParameterValue
    private ISqlContainer _readSingleSc = null!;
    private ISqlContainer _readSingleScSingle = null!;

    // Dapper SQL kept for DumpFieldTypes benchmark
    private string _dapperSql = string.Empty;

    private int _idSeed;
    private string? _fieldTypeDump;

    [Params(1, 10, 100)] public int RecordCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _factory = SqliteFactory.Instance;
        _connectionString = "Data Source=ApplesToApplesBench;Mode=Memory;Cache=Shared";

        // Sentinel connection keeps the in-memory database alive for pure mapping tests
        _sentinel = new SqliteConnection(_connectionString);
        _sentinel.Open();

        using (var cmd = _sentinel.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS benchmark (
                    id INTEGER PRIMARY KEY,
                    name VARCHAR NOT NULL,
                    age INTEGER NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }

        using (var tx = _sentinel.BeginTransaction())
        {
            using var cmd = _sentinel.CreateCommand();
            cmd.Transaction = tx;
            for (var i = 1; i <= SeedRows; i++)
            {
                cmd.CommandText =
                    "INSERT INTO benchmark (id, name, age) " +
                    $"VALUES ({i}, 'Person {i}', {20 + (i % 50)})";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        _typeMap = new TypeMapRegistry();
        _typeMap.Register<BenchEntity>();

        // Standard Context: Connection-per-op (Includes Governor + Open/Close overhead)
        var cfgStandard = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfgStandard, SqliteFactory.Instance, null, _typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);

        // SingleConnection Context: Shared connection (Pure mapping performance)
        var cfgSingle = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        };
        _pengdowsSingleContext = new DatabaseContext(cfgSingle, SqliteFactory.Instance, null, _typeMap);
        _singleGateway = new TableGateway<BenchEntity, int>(_pengdowsSingleContext);

        // Build reusable read-single containers — one per context/gateway pair.
        // Loop calls SetParameterValue("w0", id) each iteration (scalar, not array).
        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });
        _readSingleScSingle = _singleGateway.BuildRetrieve(new[] { 1 });

        // Dapper SQL kept for DumpFieldTypes diagnostic benchmark
        _dapperSql = "SELECT id, name, age FROM benchmark WHERE id = @id";
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _readSingleSc?.Dispose();
        _readSingleScSingle?.Dispose();
        _pengdowsContext?.Dispose();
        _pengdowsSingleContext?.Dispose();
        _sentinel?.Dispose();
    }

    public void Dispose()
    {
        GlobalCleanup();
    }

    /// <summary>
    /// Measures pengdows.crud in production-standard mode.
    /// Includes: Governor wait, Connection Open/Close, Command Prepare, and Mapping.
    /// </summary>
    [Benchmark]
    public async Task<int> ReadSingle_Pengdows_ProductionStandard()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            _readSingleSc.SetParameterValue("w0", id);
            var result = await _gateway.LoadSingleAsync(_readSingleSc);
            if (result != null)
            {
                hits++;
            }
        }

        return hits;
    }

    /// <summary>
    /// Measures pengdows.crud mapping engine ONLY.
    /// Eliminates Governor and Open/Close overhead by using a shared persistent connection.
    /// </summary>
    [Benchmark]
    public async Task<int> ReadSingle_Pengdows_PureMapping()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            _readSingleScSingle.SetParameterValue("w0", id);
            var result = await _singleGateway.LoadSingleAsync(_readSingleScSingle);
            if (result != null)
            {
                hits++;
            }
        }

        return hits;
    }

    /// <summary>
    /// Measures Dapper using connection-per-op.
    /// Note: Does NOT include governor overhead, making it "faster" but less safe than Standard mode.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<int> ReadSingle_Dapper_NewConnection()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            await using var conn = _factory.CreateConnection()
                                   ?? throw new InvalidOperationException("Failed to create Sqlite connection.");
            conn.ConnectionString = _connectionString;
            await conn.OpenAsync();
            var row = await conn.QuerySingleOrDefaultAsync<DapperBenchEntity>(_dapperSql, new { id });
            if (row != null)
            {
                hits++;
            }
        }

        return hits;
    }

    /// <summary>
    /// Measures Dapper mapping engine ONLY.
    /// Uses the sentinel connection to eliminate Open/Close overhead.
    /// </summary>
    [Benchmark]
    public async Task<int> ReadSingle_Dapper_PureMapping()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            var row = await _sentinel.QuerySingleOrDefaultAsync<DapperBenchEntity>(_dapperSql, new { id });
            if (row != null)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark]
    public string DumpFieldTypes()
    {
        if (_fieldTypeDump != null)
        {
            return _fieldTypeDump;
        }

        using var cmd = _sentinel.CreateCommand();
        cmd.CommandText = _dapperSql;
        var param = cmd.CreateParameter();
        param.ParameterName = "id";
        param.DbType = DbType.Int32;
        param.Value = 1;
        cmd.Parameters.Add(param);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            _fieldTypeDump = "No rows returned.";
            Console.WriteLine(_fieldTypeDump);
            return _fieldTypeDump;
        }

        var props = typeof(BenchEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var propLookup = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props)
        {
            propLookup[prop.Name] = prop;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var fieldType = reader.GetFieldType(i);
            propLookup.TryGetValue(columnName, out var prop);

            sb.Append(columnName);
            sb.Append(": field=");
            sb.Append(fieldType.Name);
            sb.Append(", property=");
            sb.Append(prop?.PropertyType.Name ?? "n/a");
            sb.AppendLine();
        }

        _fieldTypeDump = sb.ToString();
        Console.WriteLine(_fieldTypeDump);
        return _fieldTypeDump;
    }

    private int NextId()
    {
        var value = Interlocked.Increment(ref _idSeed);
        var normalized = (value % SeedRows) + 1;
        return normalized;
    }

    [Table("benchmark")]
    public sealed class BenchEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] 
        public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int32)] 
        public int Age { get; set; }
    }

    public sealed class DapperBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }
}
