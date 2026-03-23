using System.Data;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

using CrudBenchmarks;

namespace CrudBenchmarks.Internal;

/// <summary>
/// Measures the per-row cost of each SQLite storage-class → CLR type coercion.
///
/// SQLite stores all data in five storage classes:
///   INTEGER (64-bit signed), REAL (64-bit IEEE 754 double), TEXT, BLOB, NULL.
///
/// When an entity property uses a different CLR type the compiled reader plan
/// must apply a conversion on every row.  Conversion cost categories:
///
///   ZERO      — storage class matches CLR type; reader calls GetInt64/GetDouble/GetString
///               directly with no additional expression.
///   CHEAP     — compiled Expression.Convert (int64→int32, int64→short, double→float).
///   CHEAP     — compiled NotEqual(0L) check (int64 0/1 → bool).
///   MODERATE  — checked decimal arithmetic (double→decimal via Expression.Convert).
///   EXPENSIVE — string parsing (TEXT→DateTime via DateTimeOffset.Parse;
///               TEXT→Guid via Guid.Parse).
///
/// Each [Benchmark] pair isolates exactly one storage-class → CLR type mapping.
/// The "_Native" variant reads the matching CLR type (zero cost baseline).
/// The "_AsXxx" variant reads a different CLR type (shows the coercion cost).
///
/// Recommended: run with [Params(1000)] to minimise fixed overhead and reveal
/// per-row marginal cost.
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SqliteNativeTypesBenchmarks : IDisposable
{
    private const int SeedRows = 1000;
    private const string ConnStr = "Data Source=SqliteNativeTypesBench;Mode=Memory;Cache=Shared";

    // Well-known seed values that are exactly representable in IEEE 754
    // so round-trip assertions in correctness tests are exact.
    private static readonly Guid SeedGuid = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    private static readonly string SeedGuidStr = SeedGuid.ToString("D");
    private static readonly string SeedDateStr = "2024-06-15T12:00:00.0000000Z";

    // Keeps the shared-cache in-memory database alive for the benchmark run
    private SqliteConnection _sentinel = null!;
    private DatabaseContext _ctx = null!;

    // One gateway per entity variant — each entity has a unique CLR type so
    // TypeMapRegistry and the plan cache treat them independently.
    private TableGateway<IntegerNativeEntity, long> _intNativeGw = null!;
    private TableGateway<IntegerInt32Entity, long> _intInt32Gw = null!;
    private TableGateway<IntegerInt16Entity, long> _intInt16Gw = null!;
    private TableGateway<BoolNativeEntity, long> _boolNativeGw = null!;
    private TableGateway<BoolCoercedEntity, long> _boolCoercedGw = null!;
    private TableGateway<RealNativeEntity, long> _realNativeGw = null!;
    private TableGateway<RealFloatEntity, long> _realFloatGw = null!;
    private TableGateway<RealDecimalEntity, long> _realDecimalGw = null!;
    private TableGateway<TextNativeEntity, long> _textNativeGw = null!;
    private TableGateway<TextDateTimeEntity, long> _textDateTimeGw = null!;
    private TableGateway<TextGuidNativeEntity, long> _textGuidNativeGw = null!;
    private TableGateway<TextGuidEntity, long> _textGuidGw = null!;

    // Pre-built SQL strings (column selection varies; LIMIT parameter is constant @n)
    private string _sqlInt = null!;
    private string _sqlBool = null!;
    private string _sqlReal = null!;
    private string _sqlTextDt = null!;
    private string _sqlTextGu = null!;

    [Params(1, 100, 1000)]
    public int RowCount { get; set; }

    // ── Setup / Teardown ─────────────────────────────────────────────────────

    [GlobalSetup]
    public void GlobalSetup()
    {
        _sentinel = new SqliteConnection(ConnStr);
        _sentinel.Open();

        // Schema: one column per SQLite storage class, one for each variant
        using (var cmd = _sentinel.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS coercion_bench (
                    id          INTEGER PRIMARY KEY,
                    val_int     INTEGER NOT NULL,
                    val_bool    INTEGER NOT NULL,
                    val_real    REAL    NOT NULL,
                    val_text_dt TEXT    NOT NULL,
                    val_text_gu TEXT    NOT NULL
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
                    $"INSERT INTO coercion_bench (id, val_int, val_bool, val_real, val_text_dt, val_text_gu) " +
                    $"VALUES ({i}, {i}, {i % 2}, 3.14159265358979, '{SeedDateStr}', '{SeedGuidStr}')";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        var typeMap = new TypeMapRegistry();
        typeMap.Register<IntegerNativeEntity>();
        typeMap.Register<IntegerInt32Entity>();
        typeMap.Register<IntegerInt16Entity>();
        typeMap.Register<BoolNativeEntity>();
        typeMap.Register<BoolCoercedEntity>();
        typeMap.Register<RealNativeEntity>();
        typeMap.Register<RealFloatEntity>();
        typeMap.Register<RealDecimalEntity>();
        typeMap.Register<TextNativeEntity>();
        typeMap.Register<TextDateTimeEntity>();
        typeMap.Register<TextGuidNativeEntity>();
        typeMap.Register<TextGuidEntity>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = ConnStr,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.SingleConnection
        };
        _ctx = new DatabaseContext(cfg, SqliteFactory.Instance, null, typeMap);

        // Pre-build SQL (parameter marker is '@n' for SQLite; use dialect for correctness)
        var limitParam = _ctx.MakeParameterName("n");
        _sqlInt = $"SELECT id, val_int     FROM coercion_bench LIMIT {limitParam}";
        _sqlBool = $"SELECT id, val_bool    FROM coercion_bench LIMIT {limitParam}";
        _sqlReal = $"SELECT id, val_real    FROM coercion_bench LIMIT {limitParam}";
        _sqlTextDt = $"SELECT id, val_text_dt FROM coercion_bench LIMIT {limitParam}";
        _sqlTextGu = $"SELECT id, val_text_gu FROM coercion_bench LIMIT {limitParam}";

        _intNativeGw = new TableGateway<IntegerNativeEntity, long>(_ctx);
        _intInt32Gw = new TableGateway<IntegerInt32Entity, long>(_ctx);
        _intInt16Gw = new TableGateway<IntegerInt16Entity, long>(_ctx);
        _boolNativeGw = new TableGateway<BoolNativeEntity, long>(_ctx);
        _boolCoercedGw = new TableGateway<BoolCoercedEntity, long>(_ctx);
        _realNativeGw = new TableGateway<RealNativeEntity, long>(_ctx);
        _realFloatGw = new TableGateway<RealFloatEntity, long>(_ctx);
        _realDecimalGw = new TableGateway<RealDecimalEntity, long>(_ctx);
        _textNativeGw = new TableGateway<TextNativeEntity, long>(_ctx);
        _textDateTimeGw = new TableGateway<TextDateTimeEntity, long>(_ctx);
        _textGuidNativeGw = new TableGateway<TextGuidNativeEntity, long>(_ctx);
        _textGuidGw = new TableGateway<TextGuidEntity, long>(_ctx);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _ctx?.Dispose();
        _sentinel?.Dispose();
    }

    public void Dispose() => GlobalCleanup();

    // ── INTEGER column ────────────────────────────────────────────────────────

    /// <summary>
    /// ZERO COST — INTEGER → long (SQLite's native 64-bit integer).
    /// Reader calls GetInt64; no conversion expression needed.
    /// This is the baseline for all INTEGER comparisons.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<List<IntegerNativeEntity>> ReadInteger_AsLong()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlInt);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _intNativeGw.LoadListAsync(sc);
    }

    /// <summary>
    /// CHEAP — INTEGER → int (Expression.Convert int64 → int32 narrowing).
    /// One compiled cast per row; no allocation overhead.
    /// Cost vs baseline: ~0 ns at small row counts; visible at 1000+ rows.
    /// </summary>
    [Benchmark]
    public async Task<List<IntegerInt32Entity>> ReadInteger_AsInt32()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlInt);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _intInt32Gw.LoadListAsync(sc);
    }

    /// <summary>
    /// CHEAP — INTEGER → short (Expression.Convert int64 → int16 narrowing).
    /// Same overhead as int32 narrowing.
    /// </summary>
    [Benchmark]
    public async Task<List<IntegerInt16Entity>> ReadInteger_AsInt16()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlInt);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _intInt16Gw.LoadListAsync(sc);
    }

    // ── BOOLEAN (stored as INTEGER 0/1) ──────────────────────────────────────

    /// <summary>
    /// ZERO COST — INTEGER(0/1) → long.
    /// Baseline for bool comparisons: stores the raw 0/1 without interpretation.
    /// </summary>
    [Benchmark]
    public async Task<List<BoolNativeEntity>> ReadBool_AsLong()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlBool);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _boolNativeGw.LoadListAsync(sc);
    }

    /// <summary>
    /// CHEAP — INTEGER(0/1) → bool (compiled NotEqual(0L) check per row).
    /// Cost vs baseline: one comparison instruction; essentially free.
    /// </summary>
    [Benchmark]
    public async Task<List<BoolCoercedEntity>> ReadBool_AsBool()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlBool);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _boolCoercedGw.LoadListAsync(sc);
    }

    // ── REAL column ───────────────────────────────────────────────────────────

    /// <summary>
    /// ZERO COST — REAL → double (SQLite's native 64-bit float).
    /// Reader calls GetDouble; no conversion expression needed.
    /// This is the baseline for all REAL comparisons.
    /// </summary>
    [Benchmark]
    public async Task<List<RealNativeEntity>> ReadReal_AsDouble()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlReal);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _realNativeGw.LoadListAsync(sc);
    }

    /// <summary>
    /// CHEAP — REAL → float (Expression.Convert double → float narrowing).
    /// Loses ~7 decimal digits of precision; about the same cost as int narrowing.
    /// </summary>
    [Benchmark]
    public async Task<List<RealFloatEntity>> ReadReal_AsFloat()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlReal);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _realFloatGw.LoadListAsync(sc);
    }

    /// <summary>
    /// MODERATE — REAL → decimal (Expression.Convert double → decimal).
    /// decimal is a 16-byte struct; the checked arithmetic conversion is
    /// measurably slower than integral narrowing but no heap allocation occurs.
    /// </summary>
    [Benchmark]
    public async Task<List<RealDecimalEntity>> ReadReal_AsDecimal()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlReal);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _realDecimalGw.LoadListAsync(sc);
    }

    // ── TEXT column → DateTime ────────────────────────────────────────────────

    /// <summary>
    /// ZERO COST — TEXT → string (SQLite native).
    /// Baseline for the DateTime coercion comparison.
    /// </summary>
    [Benchmark]
    public async Task<List<TextNativeEntity>> ReadTextDateTime_AsString()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlTextDt);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _textNativeGw.LoadListAsync(sc);
    }

    /// <summary>
    /// EXPENSIVE — TEXT(ISO 8601) → DateTime.
    /// Compiled direct-read expression calls DateTimeOffset.Parse on every row;
    /// involves culture-invariant string parsing and UTC normalisation.
    /// This is the most expensive coercion for SQLite entities.
    /// </summary>
    [Benchmark]
    public async Task<List<TextDateTimeEntity>> ReadTextDateTime_AsDateTime()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlTextDt);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _textDateTimeGw.LoadListAsync(sc);
    }

    // ── TEXT column → Guid ────────────────────────────────────────────────────

    /// <summary>
    /// ZERO COST — TEXT → string (SQLite native).
    /// Baseline for the Guid coercion comparison.
    /// </summary>
    [Benchmark]
    public async Task<List<TextGuidNativeEntity>> ReadTextGuid_AsString()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlTextGu);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _textGuidNativeGw.LoadListAsync(sc);
    }

    /// <summary>
    /// EXPENSIVE — TEXT(UUID format "D") → Guid.
    /// Compiled direct-read expression calls Guid.Parse on every row.
    /// Cheaper than DateTime because it is a fixed-length 36-char parse.
    /// </summary>
    [Benchmark]
    public async Task<List<TextGuidEntity>> ReadTextGuid_AsGuid()
    {
        await using var sc = _ctx.CreateSqlContainer(_sqlTextGu);
        sc.AddParameterWithValue("n", DbType.Int32, RowCount);
        return await _textGuidGw.LoadListAsync(sc);
    }

    // ── Entity definitions ────────────────────────────────────────────────────
    //
    // All entities use [Id] long Id to avoid confounding the measurement with
    // an id-column coercion.  Only the measured column type varies.

    // ── INTEGER variants ──────────────────────────────────────────────────────

    [Table("coercion_bench")]
    public class IntegerNativeEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_int", DbType.Int64)] public long Val { get; set; }
    }

    [Table("coercion_bench")]
    public class IntegerInt32Entity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_int", DbType.Int32)] public int Val { get; set; }
    }

    [Table("coercion_bench")]
    public class IntegerInt16Entity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_int", DbType.Int16)] public short Val { get; set; }
    }

    // ── BOOLEAN (INTEGER 0/1) variants ────────────────────────────────────────

    [Table("coercion_bench")]
    public class BoolNativeEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_bool", DbType.Int64)] public long Val { get; set; }
    }

    [Table("coercion_bench")]
    public class BoolCoercedEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_bool", DbType.Boolean)] public bool Val { get; set; }
    }

    // ── REAL variants ─────────────────────────────────────────────────────────

    [Table("coercion_bench")]
    public class RealNativeEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_real", DbType.Double)] public double Val { get; set; }
    }

    [Table("coercion_bench")]
    public class RealFloatEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_real", DbType.Single)] public float Val { get; set; }
    }

    [Table("coercion_bench")]
    public class RealDecimalEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_real", DbType.Decimal)] public decimal Val { get; set; }
    }

    // ── TEXT variants — datetime format ───────────────────────────────────────

    [Table("coercion_bench")]
    public class TextNativeEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_text_dt", DbType.String)] public string Val { get; set; } = string.Empty;
    }

    [Table("coercion_bench")]
    public class TextDateTimeEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_text_dt", DbType.DateTime)] public DateTime Val { get; set; }
    }

    // ── TEXT variants — guid format ───────────────────────────────────────────

    [Table("coercion_bench")]
    public class TextGuidNativeEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_text_gu", DbType.String)] public string Val { get; set; } = string.Empty;
    }

    [Table("coercion_bench")]
    public class TextGuidEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("val_text_gu", DbType.Guid)] public Guid Val { get; set; }
    }
}
