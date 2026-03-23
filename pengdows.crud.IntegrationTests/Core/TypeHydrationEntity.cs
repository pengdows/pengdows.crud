using System.Data;
using pengdows.crud.attributes;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Enum used by TypeHydrationEntity — can be stored as an integer (underlying value)
/// or as a string (name) depending on the DbType declared on the column attribute.
/// </summary>
public enum TypeHydrationEnum
{
    Zero = 0,
    Alpha = 1,
    Beta = 2,
}

/// <summary>
/// Entity that carries exactly one column per distinct DbType used in pengdows.crud,
/// plus nullable variants and both integer/string enum storage modes.
/// Used exclusively for hydration-verification tests — verifies that every supported
/// CLR type survives a round-trip through every database provider.
///
/// Per-provider behavioural notes:
/// <list type="bullet">
///   <item>Oracle: empty string stored in VARCHAR2 columns is coerced to NULL.
///     Tests that write "" to <see cref="ColString"/> must accept either "" or null
///     on Oracle.</item>
///   <item>Oracle: NUMBER(5/10/19) is returned by ODP.NET as decimal; coercion to
///     short/int/long is handled by TypeCoercionHelper via Convert.ChangeType.</item>
///   <item>Oracle BINARY_FLOAT / BINARY_DOUBLE: returned as float/double by ODP.NET;
///     round-trips exactly for exactly-representable IEEE 754 values.</item>
///   <item>MySQL/MariaDB FLOAT: 32-bit single precision; DOUBLE: 64-bit double.
///     Round-trips exactly for exactly-representable values.</item>
///   <item>Firebird SMALLINT for bool: stored as 0/1; coerced to bool via
///     Convert.ChangeType on read-back.</item>
///   <item>SQLite: all integers stored as INTEGER (64-bit); all floats stored as
///     REAL (64-bit double); decimal stored as REAL — assertions use tolerance.</item>
///   <item>DateTime: always stored/retrieved as UTC; TypeCoercionHelper normalises
///     Unspecified kind to UTC on read-back.</item>
///   <item>DateTimeOffset: tests use UTC offset (TimeSpan.Zero) to avoid
///     normalisation differences across providers.</item>
/// </list>
/// </summary>
[Table("type_hydration")]
public class TypeHydrationEntity
{
    [Id][Column("id", DbType.Int64)] public long Id { get; set; }

    // ── String ───────────────────────────────────────────────────────────────
    /// <summary>Non-nullable text. Covers ASCII, whitespace, and empty-string edge cases.</summary>
    [Column("col_string", DbType.String)] public string ColString { get; set; } = string.Empty;

    /// <summary>Nullable text. Distinguishes NULL from empty string.</summary>
    [Column("col_string_null", DbType.String)] public string? ColStringNull { get; set; }

    // ── Integer types ────────────────────────────────────────────────────────
    /// <summary>16-bit signed integer (DbType.Int16 → SMALLINT/NUMBER(5)).</summary>
    [Column("col_short", DbType.Int16)] public short ColShort { get; set; }

    /// <summary>32-bit signed integer.</summary>
    [Column("col_int", DbType.Int32)] public int ColInt { get; set; }

    /// <summary>Nullable 32-bit integer. Exercises null handling for value types.</summary>
    [Column("col_int_null", DbType.Int32)] public int? ColIntNull { get; set; }

    /// <summary>64-bit signed integer.</summary>
    [Column("col_long", DbType.Int64)] public long ColLong { get; set; }

    // ── Floating-point types ─────────────────────────────────────────────────
    /// <summary>
    /// 32-bit single-precision float (DbType.Single → REAL/FLOAT/BINARY_FLOAT).
    /// Tests use exactly-representable IEEE 754 values (1.5, -3.5, 0) to allow
    /// exact equality assertions even after store-as-double / coerce-back paths.
    /// </summary>
    [Column("col_float", DbType.Single)] public float ColFloat { get; set; }

    /// <summary>
    /// 64-bit double-precision float (DbType.Double → DOUBLE PRECISION/FLOAT/BINARY_DOUBLE).
    /// Same value selection rationale as ColFloat.
    /// </summary>
    [Column("col_double", DbType.Double)] public double ColDouble { get; set; }

    /// <summary>Fixed-point decimal (DbType.Decimal → DECIMAL(18,8)/NUMBER(18,8)/REAL).</summary>
    [Column("col_decimal", DbType.Decimal)] public decimal ColDecimal { get; set; }

    // ── Boolean ──────────────────────────────────────────────────────────────
    /// <summary>Boolean — stored as BOOLEAN/BIT/NUMBER(1)/SMALLINT depending on dialect.</summary>
    [Column("col_bool", DbType.Boolean)] public bool ColBool { get; set; }

    /// <summary>Nullable boolean. Exercises null handling for bool value types.</summary>
    [Column("col_bool_null", DbType.Boolean)] public bool? ColBoolNull { get; set; }

    // ── Date / Time ──────────────────────────────────────────────────────────
    /// <summary>
    /// UTC DateTime (DbType.DateTime → TIMESTAMP/DATETIME2/TEXT).
    /// Always stored and retrieved as UTC; TypeCoercionHelper normalises
    /// Unspecified kind to UTC on read-back.
    /// </summary>
    [Column("col_datetime", DbType.DateTime)] public DateTime ColDateTime { get; set; }

    /// <summary>
    /// DateTimeOffset with UTC offset (DbType.DateTimeOffset → TIMESTAMPTZ/DATETIMEOFFSET/TIMESTAMP).
    /// All test values use TimeSpan.Zero offset to avoid provider-specific normalisation
    /// differences; assertions check only the UTC instant within 1 ms.
    /// </summary>
    [Column("col_datetimeoffset", DbType.DateTimeOffset)] public DateTimeOffset ColDateTimeOffset { get; set; }

    // ── Guid ─────────────────────────────────────────────────────────────────
    /// <summary>Guid — stored as UUID/UNIQUEIDENTIFIER/VARCHAR2(36)/CHAR(36)/TEXT.</summary>
    [Column("col_guid", DbType.Guid)] public Guid ColGuid { get; set; }

    // ── Binary ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Nullable binary payload (DbType.Binary → BYTEA/VARBINARY/RAW/BLOB).
    /// Nullable to allow NULL insertion tests without needing a separate column.
    /// </summary>
    [Column("col_binary", DbType.Binary)] public byte[]? ColBinary { get; set; }

    // ── Enum (integer storage) ────────────────────────────────────────────────
    /// <summary>
    /// Enum stored as its underlying integer value (DbType.Int32).
    /// On write: enum → Convert(underlying int). On read: int → Enum.ToObject.
    /// </summary>
    [Column("col_enum_int", DbType.Int32)] public TypeHydrationEnum ColEnumInt { get; set; }

    // ── Enum (string storage) ─────────────────────────────────────────────────
    /// <summary>
    /// Enum stored as its name string (DbType.String).
    /// On write: enum → enum.ToString(). On read: string → Enum.TryParse.
    /// </summary>
    [Column("col_enum_str", DbType.String)] public TypeHydrationEnum ColEnumStr { get; set; }
}
