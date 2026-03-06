using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using DuckDB.NET.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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
/// pengdows.crud side uses the full TableGateway API:
///   - BuildRetrieve for keyed reads (reused container + SetParameterValue)
///   - BuildBaseRetrieve + WrapObjectName for list queries (reused + SetParameterValue)
///   LIMIT values are inlined (DuckDB does not support named parameters in LIMIT).
///   Since GlobalSetup runs per [Params] value, the inlined LIMIT is always correct.
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

    // pengdows.crud — Standard mode (connection-per-op — same as EqualFootingCrudBenchmarks)
    private DatabaseContext _pengdowsCtx = null!;
    private TableGateway<BenchEntity, int> _gateway = null!;

    // pengdows.crud — SingleConnection mode (shared persistent connection, eliminates open/close overhead)
    private DatabaseContext _pengdowsSingleCtx = null!;
    private TableGateway<BenchEntity, int> _singleGateway = null!;

    private TypeMapRegistry _typeMap = null!;

    private bool _originalMatchNamesWithUnderscores;

    // Reusable ISqlContainer fields — built once in GlobalSetup, reused each iteration.
    // LIMIT is inlined since DuckDB does not support named parameters in LIMIT.
    // GlobalSetup runs per [Params] value so the inlined LIMIT is always correct.
    private ISqlContainer _readSingleSc = null!;
    private ISqlContainer _readSingleScSingle = null!;
    private ISqlContainer _readListSc = null!;
    private ISqlContainer _readListScSingle = null!;

    [Params(1, 10, 100)] public int RecordCount { get; set; }

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

        // SingleConnection: shared persistent connection — eliminates open/close + session-settings overhead.
        // Puts pengdows.crud on equal footing with the Dapper _dapperConn path.
        var cfgSingle = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={_dbFile}",
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        };
        _pengdowsSingleCtx = new DatabaseContext(cfgSingle, DuckDBClientFactory.Instance, null, _typeMap);
        _singleGateway = new TableGateway<BenchEntity, int>(_pengdowsSingleCtx);

        // ---- Build reusable containers once ----

        // ReadSingle — keyed by id collection; loop calls SetParameterValue("w0", id) (scalar)
        _readSingleSc = _gateway.BuildRetrieve(new[] { 1 });
        _readSingleScSingle = _singleGateway.BuildRetrieve(new[] { 1 });

        // ReadList — BuildBaseRetrieve + WHERE age > @Age + inlined LIMIT (DuckDB limitation)
        _readListSc = _gateway.BuildBaseRetrieve("b");
        _readListSc.Query.Append($" WHERE {_pengdowsCtx.WrapObjectName("b.age")} > ");
        _readListSc.Query.Append(_readListSc.MakeParameterName("Age"));
        _readListSc.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListSc.Query.Append($" LIMIT {RecordCount}");

        _readListScSingle = _singleGateway.BuildBaseRetrieve("b");
        _readListScSingle.Query.Append($" WHERE {_pengdowsCtx.WrapObjectName("b.age")} > ");
        _readListScSingle.Query.Append(_readListScSingle.MakeParameterName("Age"));
        _readListScSingle.AddParameterWithValue("Age", DbType.Int32, 0);
        _readListScSingle.Query.Append($" LIMIT {RecordCount}");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = _originalMatchNamesWithUnderscores;

        _readSingleSc?.Dispose();
        _readSingleScSingle?.Dispose();
        _readListSc?.Dispose();
        _readListScSingle?.Dispose();
        _pengdowsCtx?.Dispose();
        _pengdowsSingleCtx?.Dispose();
        _dapperConn?.Dispose();

        foreach (var f in new[] { _dbFile, _dbFile + ".wal" })
        {
            if (File.Exists(f))
            {
                try
                {
                    File.Delete(f);
                }
                catch
                {
                    /* best-effort */
                }
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
            _readSingleSc.SetParameterValue("w0", (i % SeedRows) + 1);
            result = await _gateway.LoadSingleAsync(_readSingleSc);
        }

        return result;
    }

    /// <summary>
    /// pengdows.crud SingleConnection — shared persistent connection, no open/close per op.
    /// On equal footing with ReadSingle_Dapper (both use a shared connection).
    /// </summary>
    [Benchmark]
    public async Task<BenchEntity?> ReadSingle_Pengdows_SingleConnection()
    {
        BenchEntity? result = null;
        for (var i = 0; i < RecordCount; i++)
        {
            _readSingleScSingle.SetParameterValue("w0", (i % SeedRows) + 1);
            result = await _singleGateway.LoadSingleAsync(_readSingleScSingle);
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
        _readListSc.SetParameterValue("Age", 30);
        return await _gateway.LoadListAsync(_readListSc);
    }

    /// <summary>
    /// pengdows.crud SingleConnection list read — shared persistent connection.
    /// </summary>
    [Benchmark]
    public async Task<List<BenchEntity>> ReadList_Pengdows_SingleConnection()
    {
        _readListScSingle.SetParameterValue("Age", 30);
        return await _singleGateway.LoadListAsync(_readListScSingle);
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

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("age", DbType.Int32)] public int Age { get; set; }

        [Column("salary", DbType.Double)] public double Salary { get; set; }

        [Column("is_active", DbType.Boolean)] public bool IsActive { get; set; }

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }
}
