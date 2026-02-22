using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DuckDB.NET.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

/// <summary>
/// Apples-to-apples read comparison on DuckDB between pengdows.crud and Dapper.
///
/// Key difference from EqualFootingCrudBenchmarks (SQLite):
///   - DuckDB returns native .NET types for all columns:
///       INTEGER  → int32   (no coercion)
///       BOOLEAN  → bool    (no coercion)
///       DOUBLE   → double  (no coercion)
///       TIMESTAMP → DateTime (no coercion — the SQLite TEXT→DateTime coercion path is bypassed)
///   - SQLite returns int64 for INTEGER, TEXT for TIMESTAMP — both require type coercion.
///
/// This benchmark isolates the coercion cost: comparing the DuckDB ratio (pengdows/Dapper)
/// against the SQLite ratio shows how much of the SQLite gap was type-conversion overhead.
///
/// Structure mirrors EqualFootingCrudBenchmarks so numbers are directly comparable.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class DuckDbReadBenchmarks : IDisposable
{
    private const int SeedRows = 1000;

    private string _dbFile = null!;

    // Dapper uses a single persistent connection (avoids per-op open/close in the Dapper path)
    private DuckDBConnection _dapperConn = null!;

    // pengdows.crud uses Standard mode (connection-per-op — same as EqualFootingCrudBenchmarks)
    private DatabaseContext _pengdowsCtx = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;
    private TypeMapRegistry _typeMap = null!;

    private bool _originalMatchNamesWithUnderscores;

    [Params(1, 10, 100)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _originalMatchNamesWithUnderscores = DefaultTypeMap.MatchNamesWithUnderscores;
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _dbFile = Path.Combine(
            Path.GetTempPath(),
            $"pengdows_duckdb_bench_{Guid.NewGuid():N}.duckdb");

        // Seed via a direct DuckDB connection (also serves as the Dapper benchmark connection)
        _dapperConn = new DuckDBConnection($"Data Source={_dbFile}");
        _dapperConn.Open();

        _dapperConn.Execute(@"
            CREATE TABLE benchmark (
                id        INTEGER   PRIMARY KEY,
                name      VARCHAR   NOT NULL,
                age       INTEGER   NOT NULL,
                salary    DOUBLE    NOT NULL,
                is_active BOOLEAN   NOT NULL,
                created_at TIMESTAMP NOT NULL
            )");

        // Bulk-insert seed rows with a single multi-row VALUES statement
        var rows = Enumerable.Range(1, SeedRows).Select(i =>
            $"({i}, 'Person {i}', {20 + i % 50}, {30000.0 + i * 100.0:F1}, " +
            $"{(i % 2 == 0 ? "true" : "false")}, " +
            $"'{DateTime.UtcNow.AddDays(-i):yyyy-MM-dd HH:mm:ss}')");

        _dapperConn.Execute(
            "INSERT INTO benchmark (id, name, age, salary, is_active, created_at) VALUES " +
            string.Join(",", rows));

        // pengdows.crud — Standard mode (connection-per-op, mirrors EqualFootingCrudBenchmarks)
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<BenchEntity>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={_dbFile}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };
        _pengdowsCtx = new DatabaseContext(cfg, DuckDBClientFactory.Instance, null, _typeMap);
        _gateway = new TableGateway<BenchEntity, int>(_pengdowsCtx);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;
        _pengdowsCtx?.Dispose();
        _dapperConn?.Dispose();

        foreach (var f in new[] { _dbFile, _dbFile + ".wal" })
        {
            if (File.Exists(f))
            {
                try { File.Delete(f); } catch { /* best-effort */ }
            }
        }
    }

    public void Dispose() => GlobalCleanup();

    // ========================================================================
    // READ SINGLE
    // Opens a new connection per call (pengdows Standard) or reuses the
    // sentinel connection (Dapper) — same structure as EqualFootingCrudBenchmarks.
    // ========================================================================

    [Benchmark(Baseline = true)]
    public async Task<BenchEntity?> ReadSingle_Pengdows()
    {
        BenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            await using var container = _pengdowsCtx.CreateSqlContainer();
            container.Query.Append(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = ");
            container.Query.Append(container.MakeParameterName("Id"));
            container.AddParameterWithValue("Id", DbType.Int32, (i % SeedRows) + 1);
            result = await _gateway.LoadSingleAsync(container);
        }

        return result;
    }

    [Benchmark]
    public async Task<BenchEntity?> ReadSingle_Dapper()
    {
        BenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            result = await _dapperConn.QueryFirstOrDefaultAsync<BenchEntity>(
                "SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE id = @Id",
                new { Id = (i % SeedRows) + 1 });
        }

        return result;
    }

    // ========================================================================
    // READ LIST
    // ========================================================================

    [Benchmark]
    public async Task<List<BenchEntity>> ReadList_Pengdows()
    {
        await using var container = _pengdowsCtx.CreateSqlContainer();
        // DuckDB does not support named parameters in LIMIT; inline the value directly.
        container.Query.Append(
            $"SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > ");
        container.Query.Append(container.MakeParameterName("Age"));
        container.Query.Append($" LIMIT {RecordCount}");
        container.AddParameterWithValue("Age", DbType.Int32, 30);
        return await _gateway.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<BenchEntity>> ReadList_Dapper()
    {
        var rows = await _dapperConn.QueryAsync<BenchEntity>(
            // DuckDB does not support named parameters in LIMIT; inline the value directly.
            $"SELECT id, name, age, salary, is_active, created_at FROM benchmark WHERE age > @Age LIMIT {RecordCount}",
            new { Age = 30 });
        return rows.AsList();
    }

    // ========================================================================
    // ENTITY
    // CreatedAt is DateTime (not string) — DuckDB TIMESTAMP returns DateTime
    // directly; no coercion required. Contrast with SQLite TEXT columns.
    // ========================================================================

    [Table("benchmark")]
    public class BenchEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int32)]
        public int Age { get; set; }

        [Column("salary", DbType.Double)]
        public double Salary { get; set; }

        [Column("is_active", DbType.Boolean)]
        public bool IsActive { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }
}
