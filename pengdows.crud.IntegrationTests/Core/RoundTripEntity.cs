using System.Data;
using pengdows.crud.attributes;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Entity used for full row round-trip fidelity tests. Covers every column type
/// that diverges across providers: unicode text, nullable vs. empty string,
/// decimal precision, DateTimeOffset, Guid, and binary.
///
/// Per-provider behavioural notes (for use in test assertions):
/// <list type="bullet">
///   <item>Oracle: empty string ('') is coerced to NULL. When TextNullable = ""
///     is stored, assert that the retrieved value is null, not "".</item>
///   <item>Oracle: DateTimeOffset is normalised to UTC; offset is discarded.
///     Assert only the UTC instant within a 1 ms tolerance.</item>
///   <item>MySQL / MariaDB: no tz-aware column type. DateTimeOffset is
///     normalised to UTC before storage; the offset is discarded.
///     Assert only the UTC instant within a 1 ms tolerance.</item>
///   <item>Firebird: DateTimeOffset stored as plain TIMESTAMP (UTC).
///     Same assertion as MySQL / MariaDB.</item>
///   <item>SQLite: DateTimeOffset stored as ISO-8601 TEXT; sub-millisecond
///     precision may be lost. Assert within 1 ms.</item>
///   <item>Snowflake: DateTimeOffset stored as TIMESTAMP_NTZ; offset is
///     normalised to UTC. Assert only the UTC instant within 1 ms.</item>
///   <item>SQLite: DecimalValue stored as REAL (IEEE 754 double); precision
///     is limited to ~15 significant digits. Assert with 1e-7 tolerance.</item>
///   <item>Oracle: GuidValue stored as VARCHAR2(36); round-trips exactly.</item>
///   <item>MySQL / MariaDB / SQLite / Firebird: GuidValue stored as CHAR(36)
///     or TEXT; round-trips exactly as a string representation.</item>
///   <item>Snowflake: GuidValue stored as VARCHAR(36); round-trips exactly.</item>
/// </list>
/// </summary>
[Table("round_trip_entity")]
public class RoundTripEntity
{
    [Id] [Column("id", DbType.Int64)] public long Id { get; set; }

    /// <summary>
    /// Latin / ASCII text. Used to assert both basic ASCII survival and
    /// leading/trailing whitespace preservation.
    /// </summary>
    [Column("text_value", DbType.String)]
    public string TextValue { get; set; } = string.Empty;

    /// <summary>
    /// Unicode text including non-BMP codepoints (CJK, emoji).
    /// MySQL / MariaDB DDL declares this column as utf8mb4 to support 4-byte
    /// codepoints. Other providers use their default (typically UTF-8).
    /// </summary>
    [Column("text_unicode", DbType.String)]
    public string TextUnicode { get; set; } = string.Empty;

    /// <summary>
    /// Nullable string. Distinguishes NULL from empty string.
    /// Oracle coerces '' to NULL — tests must accept either null or "" on Oracle.
    /// </summary>
    [Column("text_nullable", DbType.String)]
    public string? TextNullable { get; set; }

    /// <summary>32-bit signed integer.</summary>
    [Column("int_value", DbType.Int32)]
    public int IntValue { get; set; }

    /// <summary>64-bit signed integer — exercised near max range.</summary>
    [Column("long_value", DbType.Int64)]
    public long LongValue { get; set; }

    /// <summary>
    /// Decimal stored as DECIMAL(18,8) in the schema. Test values should
    /// cover precision / scale edges: 123456789.12345678, 0.00000001,
    /// -99999999.99999999.
    /// SQLite stores as REAL (IEEE 754 double); assertions must tolerate
    /// ~1e-7 relative error and avoid values with more than 15 significant digits.
    /// </summary>
    [Column("decimal_value", DbType.Decimal)]
    public decimal DecimalValue { get; set; }

    /// <summary>
    /// Boolean — stored as BOOLEAN / BIT / NUMBER(1) / SMALLINT depending on dialect.
    /// Both true and false must round-trip correctly and work in WHERE predicates.
    /// </summary>
    [Column("bool_value", DbType.Boolean)]
    public bool BoolValue { get; set; }

    /// <summary>
    /// DateTimeOffset with a non-UTC offset and microsecond precision.
    /// Providers without tz-aware storage (MySQL, MariaDB, Firebird) will
    /// normalise to UTC and discard the offset. Tests must skip Offset
    /// comparison on those providers and assert only the UTC instant within
    /// a 1 ms tolerance.
    /// </summary>
    [Column("datetimeoffset_value", DbType.DateTimeOffset)]
    public DateTimeOffset DateTimeOffsetValue { get; set; }

    /// <summary>
    /// Guid — stored as UNIQUEIDENTIFIER (SQL Server), UUID (PostgreSQL /
    /// DuckDB / CockroachDB), VARCHAR2(36) (Oracle), or CHAR(36) (MySQL / MariaDB /
    /// SQLite / Firebird). Must round-trip exactly regardless of storage form.
    /// </summary>
    [Column("guid_value", DbType.Guid)]
    public Guid GuidValue { get; set; }

    /// <summary>
    /// Binary payload — stored as BYTEA (PostgreSQL), VARBINARY(256) (SQL Server /
    /// MySQL / MariaDB / Firebird), RAW(256) (Oracle), BLOB (SQLite / DuckDB),
    /// or BYTES (CockroachDB). Must survive byte-for-byte.
    /// </summary>
    [Column("binary_value", DbType.Binary)]
    public byte[] BinaryValue { get; set; } = Array.Empty<byte>();
}
