// =============================================================================
// FILE: DatabaseTraits.cs
// PURPOSE: Per-database knowledge that is needed without a dialect instance.
//
// AI SUMMARY:
// - REV-039: code outside dialects/ never names a SupportedDatabase value. What the shared
//   type registries, value converters and the exception-translator registry need to know about
//   a database is declared here, by the dialect class that hosts it, and looked up by value.
// - Each dialect class has a static Create<Name>Traits() factory; SqlDialectFactory.CreateTraits
//   maps every SupportedDatabase value to its factory, next to the dialect-creating switch.
// - For(database): one array index (no allocation) for hot paths such as the converters.
//   Unknown, combined flags and unlisted values get the Sql92 fallback, exactly like the
//   dialect factory's default arm.
// - All: one entry per database, used once at startup by AdvancedTypeRegistry and
//   CoercionRegistry to collect each database's registrations.
// =============================================================================

using pengdows.crud;
using System.Linq.Expressions;
using System.Numerics;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.types;
using pengdows.crud.types.coercion;

namespace pengdows.crud.dialects;

/// <summary>How a database takes a <c>Geometry</c>/<c>Geography</c> parameter value.</summary>
internal enum SpatialWireFormat
{
    /// <summary>The value's provider object if any, else its WKB, WKT or GeoJSON as carried.</summary>
    Unspecified = 0,

    /// <summary>Big-endian SRID + WKB that the gateway SQL turns into the instance (STGeomFromWKB).</summary>
    SridPrefixedWkbForConstructor,

    /// <summary>EWKB bytes (PostGIS and compatible built-in spatial types).</summary>
    ExtendedWkb,

    /// <summary>The server's internal format: a 4-byte little-endian SRID, then WKB (both directions).</summary>
    LittleEndianSridPrefixedWkb,

    /// <summary>WKT text only.</summary>
    WellKnownText,

    /// <summary>EWKT, else EWKB as hex, else GeoJSON text.</summary>
    ExtendedTextOrHex,

    /// <summary>
    /// EWKB as VARBINARY whose SRID flag is set even for SRID 0 (WKT is encoded to WKB); the dialect
    /// builds the value with a constructor that requires the flag (ST_GeomFromEWKB).
    /// </summary>
    SridFlaggedExtendedWkb,

    /// <summary>EWKT text, always with its SRID (the dialect builds the native object from it).</summary>
    ExtendedWellKnownText
}

/// <summary>How a database takes the <c>IntervalYearMonth</c>/<c>IntervalDaySecond</c> value objects.</summary>
internal enum IntervalWireFormat
{
    /// <summary>
    /// The value object itself (year-month). A database with no interval type of its own gets the ISO 8601 text for a
    /// day-second value (see the interval converters).
    /// </summary>
    Unspecified = 0,

    /// <summary>Oracle INTERVAL literal text.</summary>
    OracleLiteral,

    /// <summary>ISO 8601 duration text.</summary>
    Iso8601,

    /// <summary>
    /// The PostgreSQL interval: months, days and microseconds, sent as the Npgsql interval the
    /// <see cref="PostgreSqlInterval"/> converter builds. Keeps months, which text through a text parameter does not
    /// (Npgsql refuses a string on an interval parameter, and the interval type cannot be cast from text).
    /// </summary>
    PostgreSqlInterval,

    /// <summary>
    /// A native day-time interval that takes the value as a <see cref="TimeSpan"/> (DuckDB's INTERVAL, whose days are kept).
    /// </summary>
    DayTimeSpan,

    /// <summary>
    /// Informix's interval literals: "D HH:MM:SS" (a fraction only when the value has one) for INTERVAL DAY TO SECOND, and
    /// "Y-MM" for INTERVAL YEAR TO MONTH. Both refuse the ISO 8601 form.
    /// </summary>
    InformixLiteral
}

/// <summary>
/// Reads one column declared with a native type: <paramref name="reader"/> and <paramref name="ordinal"/> are the reader and
/// the column, and the result converts to <paramref name="targetType"/>. Null when the rule does not cover the column.
/// </summary>
internal delegate Expression? NativeColumnReadRule(Expression reader, Expression ordinal, string nativeType,
    Type targetType, TypeCoercionOptions? options);

/// <summary>
/// What code outside the dialects needs to know about one database, declared by its dialect.
/// </summary>
internal sealed class DatabaseTraits
{
    private static readonly DatabaseTraits[] Table = BuildTable();

    // Table without the fallback entries that fill unused bit positions.
    private static readonly DatabaseTraits[] Entries = Table.Distinct().ToArray();

    internal DatabaseTraits(SupportedDatabase database, IDbExceptionTranslator exceptionTranslator)
    {
        Database = database;
        ExceptionTranslator = exceptionTranslator;
    }

    /// <summary>The database these traits describe.</summary>
    public SupportedDatabase Database { get; }

    /// <summary>Turns this database's provider exceptions into the typed exception hierarchy.</summary>
    public IDbExceptionTranslator ExceptionTranslator { get; }

    public SpatialWireFormat SpatialFormat { get; init; }

    public IntervalWireFormat IntervalFormat { get; init; }

    /// <summary>
    /// True when the provider's text reads can end in a NUL that is padding, not data (Informix's TEXT and CLOB, confirmed
    /// live): a string read drops that trailing NUL. SQL text never ends in one.
    /// </summary>
    public bool TextHasTrailingNul { get; init; }

    /// <summary>
    /// True when the provider writes a time with its offset as text (SQLite: "HH:mm:ss.fffffff+hh:mm"), so a time-only text is that
    /// time and reads back as one. Other providers' time-only text is not a value the library can date.
    /// </summary>
    public bool TimeWithZoneIsText { get; init; }

    /// <summary>
    /// Inet, Cidr, PostgreSqlInterval and Range values are sent as Npgsql's own types
    /// (NpgsqlInet, NpgsqlCidr, NpgsqlInterval, NpgsqlRange) when Npgsql is loaded.
    /// </summary>
    public bool BindsNpgsqlValueTypes { get; init; }

    /// <summary>
    /// The database has a cidr type, so a CIDR is bound as Npgsql's NpgsqlCidr. CockroachDB takes its network as inet and
    /// refuses NpgsqlDbType.Cidr; its CIDR is bound as text.
    /// </summary>
    public bool BindsNpgsqlCidr { get; init; }

    /// <summary>
    /// A binary JSON column (SingleStore's BSON) returns the binary document, read as its JSON text.
    /// </summary>
    public bool BinaryJsonIsBson { get; init; }

    /// <summary>Adds this database's parameter mappings to the shared type registry.</summary>
    public Action<AdvancedTypeRegistry>? RegisterTypeMappings { get; init; }

    /// <summary>Adds this database's read coercions to a coercion registry.</summary>
    public Action<CoercionRegistry>? RegisterCoercions { get; init; }

    /// <summary>
    /// The read of a column the driver returns in a provider-specific way, for a column declared with a native type (a BIT(n)
    /// width, an Oracle BFILE locator). Null when the database has no such column. Returns null for a column the rule does
    /// not cover, which then takes the general read.
    /// </summary>
    public NativeColumnReadRule? NativeColumnRead { get; init; }

    /// <summary>One entry per <see cref="SupportedDatabase"/> value, Unknown first.</summary>
    public static IReadOnlyList<DatabaseTraits> All => Entries;

    /// <summary>
    /// The traits for <paramref name="database"/>; Unknown, combined flags and unlisted values
    /// get the fallback (Sql92) traits, as the dialect factory gives them the Sql92 dialect.
    /// </summary>
    public static DatabaseTraits For(SupportedDatabase database)
    {
        var bits = (uint)database;
        if (bits != 0 && (bits & (bits - 1)) == 0)
        {
            var index = BitOperations.TrailingZeroCount(bits) + 1;
            if (index < Table.Length)
            {
                return Table[index];
            }
        }

        return Table[0];
    }

    // SupportedDatabase is a flags enum: Unknown = 0, every database one bit. Index 0 holds
    // Unknown and index n + 1 the database at bit n, so For() is a bit count and an array read.
    private static DatabaseTraits[] BuildTable()
    {
        var databases = Enum.GetValues<SupportedDatabase>()
            .Where(d => d != SupportedDatabase.Unknown && BitOperations.IsPow2((uint)d))
            .ToArray();
        var table = new DatabaseTraits[databases.Max(d => BitOperations.TrailingZeroCount((uint)d)) + 2];
        table[0] = SqlDialectFactory.CreateTraits(SupportedDatabase.Unknown);
        foreach (var database in databases)
        {
            table[BitOperations.TrailingZeroCount((uint)database) + 1] = SqlDialectFactory.CreateTraits(database);
        }

        for (var i = 1; i < table.Length; i++)
        {
            table[i] ??= table[0];
        }

        return table;
    }
}
