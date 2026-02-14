using System.Data;
using System.Data.Common;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Apples-to-apples comparison between Dapper and pengdows.crud:
/// - Same SQLite shared-cache in-memory database
/// - New connection per operation (factory -> connection string -> open)
/// - Prebuilt SQL reused across iterations (no per-iteration SQL generation)
/// - Per-iteration cost is parameters + execution + mapping
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ApplesToApplesDapperBenchmarks : IDisposable
{
    private const string ConnStr = "Data Source=ApplesToApplesBench;Mode=Memory;Cache=Shared";
    private const int SeedRows = 1000;

    private SqliteConnection _sentinel = null!;

    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    private DbProviderFactory _factory = null!;

    private string _pengdowsSql = string.Empty;
    private string _dapperSql = string.Empty;

    private int _idSeed;

    [Params(1, 10, 100)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _factory = SqliteFactory.Instance;

        // Sentinel connection keeps shared-cache in-memory DB alive
        _sentinel = new SqliteConnection(ConnStr);
        _sentinel.Open();

        using (var cmd = _sentinel.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS benchmark (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
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
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = ConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsContext = new DatabaseContext(cfg, SqliteFactory.Instance, null, _typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsContext);

        _pengdowsSql = BuildSingleReadSql(p => _pengdowsContext.MakeParameterName(p));
        _dapperSql = BuildSingleReadSql(p => $"@{p}");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _pengdowsContext?.Dispose();
        _sentinel?.Dispose();
    }

    public void Dispose()
    {
        GlobalCleanup();
    }

    [Benchmark(Baseline = true)]
    public async Task<int> ReadSingle_Pengdows_ReadySql_NewConnection()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            await using var container = _pengdowsContext.CreateSqlContainer(_pengdowsSql);
            container.AddParameterWithValue("id", DbType.Int32, id);
            var result = await _gateway.LoadSingleAsync(container);
            if (result != null)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark]
    public async Task<int> ReadSingle_Dapper_ReadySql_NewConnection()
    {
        var hits = 0;
        for (var i = 0; i < RecordCount; i++)
        {
            var id = NextId();
            await using var conn = _factory.CreateConnection()
                ?? throw new InvalidOperationException("Failed to create SQLite connection.");
            conn.ConnectionString = ConnStr;
            await conn.OpenAsync();
            var row = await conn.QuerySingleOrDefaultAsync<DapperBenchEntity>(_dapperSql, new { id });
            if (row != null)
            {
                hits++;
            }
        }

        return hits;
    }

    private int NextId()
    {
        var value = Interlocked.Increment(ref _idSeed);
        var normalized = (value % SeedRows) + 1;
        return normalized;
    }

    private static string BuildSingleReadSql(Func<string, string> param)
    {
        return "SELECT id, name, age FROM benchmark WHERE id = " + param("id");
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
