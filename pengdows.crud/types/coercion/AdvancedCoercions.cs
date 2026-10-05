// =============================================================================
// FILE: AdvancedCoercions.cs
// PURPOSE: Advanced type coercions for database-specific exotic types.
//
// AI SUMMARY:
// - Contains coercion implementations for complex database-specific types.
// - RegisterAll(): Registers all advanced coercions with a CoercionRegistry.
// - Temporal types:
//   * PostgreSqlIntervalCoercion: PostgreSQL INTERVAL type
//   * IntervalYearMonthCoercion: Oracle INTERVAL YEAR TO MONTH
//   * IntervalDaySecondCoercion: Oracle INTERVAL DAY TO SECOND
// - Network types (PostgreSQL):
//   * InetCoercion: IP address with optional netmask (inet)
//   * CidrCoercion: Network address (cidr)
//   * MacAddressCoercion: MAC hardware address (macaddr)
// - Spatial types:
//   * GeometryCoercion: WKB/WKT/GeoJSON to Geometry
//   * GeographyCoercion: WKB/WKT/GeoJSON to Geography
// - Range types:
//   * PostgreSqlRangeIntCoercion, PostgreSqlRangeDateTimeCoercion, PostgreSqlRangeLongCoercion
// - Concurrency: RowVersionValueCoercion (SQL Server timestamp/rowversion)
// - LOBs: BlobStreamCoercion (binary), ClobStreamCoercion (character)
// - Handles provider-specific types via reflection (e.g., NpgsqlInet).
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Advanced type coercions for database-specific types.
/// Handles spatial, network, temporal, and large object types.
/// </summary>
internal static class AdvancedCoercions
{
    // Reading an NpgsqlInterval/NpgsqlRange back does not depend on the database: the converters
    // use their database argument only when writing.
    internal const SupportedDatabase AnyDatabase = SupportedDatabase.Unknown;

    public static void RegisterAll(CoercionRegistry registry)
    {
        // Temporal types
        registry.Register(new PostgreSqlIntervalCoercion());
        registry.Register(new IntervalYearMonthCoercion());
        registry.Register(new IntervalDaySecondCoercion());

        // Network types
        registry.Register(new InetCoercion());
        registry.Register(new CidrCoercion());
        registry.Register(new MacAddressCoercion());

        // Spatial types
        registry.Register(new GeometryCoercion());
        registry.Register(new GeographyCoercion());
        // Database-specific coercions (e.g. the MySQL family's internal spatial format, TYPE-018;
        // SingleStore's packed float32 vectors, TYPE-002) are declared by the dialects (REV-039).
        foreach (var traits in DatabaseTraits.All)
        {
            traits.RegisterCoercions?.Invoke(registry);
        }

        // Range types (generic)
        registry.Register(new PostgreSqlRangeIntCoercion());
        registry.Register(new PostgreSqlRangeDateTimeCoercion());
        registry.Register(new PostgreSqlRangeLongCoercion());

        // Concurrency/versioning
        registry.Register(new RowVersionValueCoercion());

        // Hierarchies (TYPE-016)
        registry.Register(new HierarchyIdCoercion());

        // Large object types (LOBs)
        registry.Register(new BlobStreamCoercion());
        registry.Register(new ClobStreamCoercion());
    }
}

/// <summary>
/// Coercion for PostgreSQL INTERVAL type.
/// </summary>
internal class PostgreSqlIntervalCoercion : DbCoercion<PostgreSqlInterval>
{
    public override bool TryRead(in DbValue src, out PostgreSqlInterval value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        var raw = src.RawValue;
        if (raw is null)
        {
            value = default;
            return false;
        }

        switch (raw)
        {
            case PostgreSqlInterval interval:
                value = interval;
                return true;
            case TimeSpan ts:
                value = PostgreSqlInterval.FromTimeSpan(ts);
                return true;
            case not null when raw.GetType().FullName == "NpgsqlTypes.NpgsqlInterval":
                // Npgsql's full-fidelity interval (months/days/microseconds) — same conversion the
                // gateway's hydration path uses, so both read paths agree. Strings are not accepted
                // here: the converter's ISO parser treats unrecognized text as zero (backlog D01).
                return IntervalConverter.TryConvertFromProvider(raw, AdvancedCoercions.AnyDatabase, out value);
            default:
                value = default;
                return false;
        }
    }

    private static readonly PostgreSqlIntervalConverter IntervalConverter = new();

    public override bool TryWrite([AllowNull] PostgreSqlInterval value, DbParameter parameter)
    {
        parameter.Value = value.ToTimeSpan();
        parameter.DbType = DbType.Object;
        return true;
    }
}

/// <summary>
/// Coercion for Oracle/PostgreSQL INTERVAL YEAR TO MONTH type.
/// </summary>
internal class IntervalYearMonthCoercion : DbCoercion<IntervalYearMonth>
{
    public override bool TryRead(in DbValue src, out IntervalYearMonth value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        var raw = src.RawValue;
        if (raw is null)
        {
            value = default;
            return false;
        }

        switch (raw)
        {
            case IntervalYearMonth interval:
                value = interval;
                return true;
            // ODP.NET exposes INTERVAL YEAR TO MONTH as the provider's numeric month count.
            case int totalMonths:
                value = IntervalYearMonth.FromTotalMonths(totalMonths);
                return true;
            case long totalMonths:
                value = IntervalYearMonth.FromTotalMonths(checked((int)totalMonths));
                return true;
            case string text:
                try
                {
                    value = IntervalYearMonth.Parse(text);
                    return true;
                }
                catch
                {
                    value = default;
                    return false;
                }
            default:
                value = default;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] IntervalYearMonth value, DbParameter parameter)
    {
        // Format as ISO 8601 duration: P{years}Y{months}M
        var formatted = $"P{value.Years}Y{value.Months}M";
        parameter.Value = formatted;
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for Oracle/PostgreSQL INTERVAL DAY TO SECOND type.
/// </summary>
internal class IntervalDaySecondCoercion : DbCoercion<IntervalDaySecond>
{
    public override bool TryRead(in DbValue src, out IntervalDaySecond value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        var raw = src.RawValue;
        if (raw is null)
        {
            value = default;
            return false;
        }

        switch (raw)
        {
            case IntervalDaySecond interval:
                value = interval;
                return true;
            case TimeSpan ts:
                value = IntervalDaySecond.FromTimeSpan(ts);
                return true;
            case string text:
                try
                {
                    value = IntervalDaySecond.Parse(text);
                    return true;
                }
                catch
                {
                    value = default;
                    return false;
                }
            default:
                value = default;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] IntervalDaySecond value, DbParameter parameter)
    {
        // For most databases, write as TimeSpan-compatible
        parameter.Value = value.TotalTime;
        parameter.DbType = DbType.Object;
        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL INET type (IP address with optional netmask).
/// </summary>
internal class InetCoercion : DbCoercion<Inet>
{
    public override bool TryRead(in DbValue src, out Inet value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        var raw = src.RawValue;
        if (raw is null)
        {
            value = default;
            return false;
        }

        switch (raw)
        {
            case Inet inet:
                value = inet;
                return true;
            case string text:
                try
                {
                    value = Inet.Parse(text);
                    return true;
                }
                catch
                {
                    value = default;
                    return false;
                }
            case IPAddress address:
                value = new Inet(address);
                return true;
            default:
                // Handle provider-specific types (e.g., NpgsqlInet)
                var type = raw.GetType();
                if (type.FullName?.Contains("Inet", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var addressProp = type.GetProperty("Address");
                    var netmaskProp = type.GetProperty("Netmask");
                    if (addressProp?.GetValue(raw) is IPAddress addr)
                    {
                        byte? prefix = null;
                        if (netmaskProp?.GetValue(raw) is byte netmask)
                        {
                            prefix = netmask;
                        }

                        value = Inet.FromProvider(addr, prefix);
                        return true;
                    }
                }

                value = default;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] Inet value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for <see cref="HierarchyId"/> (TYPE-016): reads SQL Server's stored encoding (what
/// SqlClient's GetBytes returns without Microsoft.SqlServer.Types), that assembly's SqlHierarchyId,
/// or the text form; writes the text form, which SQL Server converts to hierarchyid implicitly.
/// </summary>
internal class HierarchyIdCoercion : DbCoercion<HierarchyId>
{
    public override bool TryRead(in DbValue src, out HierarchyId value)
    {
        value = default;
        if (src.IsNull)
        {
            return false;
        }

        try
        {
            switch (src.RawValue)
            {
                case HierarchyId hierarchyId:
                    value = hierarchyId;
                    return true;
                case string text:
                    return HierarchyId.TryParse(text, out value);
                case byte[] bytes:
                    value = HierarchyId.FromSqlServerBytes(bytes);
                    return true;
                case { } other when other.GetType().Name == "SqlHierarchyId":
                    return HierarchyId.TryParse(other.ToString(), out value);
                default:
                    return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public override bool TryWrite([AllowNull] HierarchyId value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL CIDR type (network address).
/// </summary>
internal class CidrCoercion : DbCoercion<Cidr>
{
    public override bool TryRead(in DbValue src, out Cidr value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        var raw = src.RawValue;
        if (raw is null)
        {
            value = default;
            return false;
        }

        switch (raw)
        {
            case Cidr cidr:
                value = cidr;
                return true;
            case string text:
                try
                {
                    value = Cidr.Parse(text);
                    return true;
                }
                catch
                {
                    value = default;
                    return false;
                }
            default:
                // Handle provider-specific types
                var type = raw.GetType();
                if (type.FullName?.Contains("Cidr", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var addressProp = type.GetProperty("Address");
                    var netmaskProp = type.GetProperty("Netmask");
                    if (addressProp?.GetValue(raw) is IPAddress addr &&
                        netmaskProp?.GetValue(raw) is byte prefix)
                    {
                        value = new Cidr(addr, prefix);
                        return true;
                    }
                }

                value = default;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] Cidr value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL MACADDR type.
/// </summary>
internal class MacAddressCoercion : DbCoercion<MacAddress>
{
    public override bool TryRead(in DbValue src, out MacAddress value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        var raw = src.RawValue;
        if (raw is null)
        {
            value = default;
            return false;
        }

        switch (raw)
        {
            case MacAddress mac:
                value = mac;
                return true;
            case string text:
                try
                {
                    value = MacAddress.Parse(text);
                    return true;
                }
                catch
                {
                    value = default;
                    return false;
                }
            case PhysicalAddress physical:
                value = new MacAddress(physical);
                return true;
            default:
                // Handle provider-specific types
                var type = raw.GetType();
                if (type.FullName?.Contains("MacAddress", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var addressProp = type.GetProperty("Address");
                    if (addressProp?.GetValue(raw) is PhysicalAddress addr)
                    {
                        value = new MacAddress(addr);
                        return true;
                    }
                }

                value = default;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] MacAddress value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for spatial GEOMETRY type.
/// </summary>
internal class GeometryCoercion : DbCoercion<Geometry>
{
    public override bool TryRead(in DbValue src, out Geometry value)
    {
        if (src.IsNull)
        {
            value = default!;
            return false;
        }

        try
        {
            switch (src.RawValue)
            {
                case Geometry geom:
                    value = geom;
                    return true;
                case byte[] bytes:
                    GeometryConverter.ExtractSridFromEwkb(bytes, out var srid, out var normalized);
                    value = Geometry.FromWellKnownBinary(normalized, srid);
                    return true;
                case string text when text.StartsWith("{"):
                    value = Geometry.FromGeoJson(text, 0);
                    return true;
                case string text:
                    // EWKT keeps its SRID (Oracle SDO_GEOMETRY is read this way, TYPE-021).
                    var (textSrid, wkt) = GeometryConverter.ExtractSridFromText(text);
                    value = Geometry.FromWellKnownText(wkt, textSrid);
                    return true;
                default:
                    value = default!;
                    return false;
            }
        }
        catch
        {
            value = default!;
            return false;
        }
    }

    public override bool TryWrite([AllowNull] Geometry value, DbParameter parameter)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Binary;
            return true;
        }

        if (!value.WellKnownBinary.IsEmpty)
        {
            parameter.Value = SpatialConverter<Geometry>.AddSridToWkb(value.WellKnownBinary.Span, value.Srid);
            parameter.DbType = DbType.Binary;
        }
        else
        {
            parameter.Value = SpatialConverter<Geometry>.AddSridToWkt(value.WellKnownText!, value.Srid);
            parameter.DbType = DbType.String;
        }

        return true;
    }
}

/// <summary>
/// Coercion for spatial GEOGRAPHY type.
/// </summary>
internal class GeographyCoercion : DbCoercion<Geography>
{
    public override bool TryRead(in DbValue src, out Geography value)
    {
        if (src.IsNull)
        {
            value = default!;
            return false;
        }

        try
        {
            switch (src.RawValue)
            {
                case Geography geog:
                    value = geog;
                    return true;
                case byte[] bytes:
                    GeographyConverter.ExtractSridFromEwkb(bytes, out var srid, out var normalized);
                    value = Geography.FromWellKnownBinary(normalized, srid == 0 ? 4326 : srid);
                    return true;
                case string text when text.StartsWith("{"):
                    value = Geography.FromGeoJson(text, 4326);
                    return true;
                case string text:
                    var (textSrid, wkt) = GeographyConverter.ExtractSridFromText(text);
                    value = Geography.FromWellKnownText(wkt, textSrid == 0 ? 4326 : textSrid);
                    return true;
                default:
                    value = default!;
                    return false;
            }
        }
        catch
        {
            value = default!;
            return false;
        }
    }

    public override bool TryWrite([AllowNull] Geography value, DbParameter parameter)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Binary;
            return true;
        }

        if (!value.WellKnownBinary.IsEmpty)
        {
            parameter.Value = SpatialConverter<Geography>.AddSridToWkb(value.WellKnownBinary.Span, value.Srid);
            parameter.DbType = DbType.Binary;
        }
        else
        {
            parameter.Value = SpatialConverter<Geography>.AddSridToWkt(value.WellKnownText!, value.Srid);
            parameter.DbType = DbType.String;
        }

        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL Range&lt;int&gt; type.
/// </summary>
internal class PostgreSqlRangeIntCoercion : DbCoercion<Range<int>>
{
    public override bool TryRead(in DbValue src, out Range<int> value)
    {
        if (src.IsNull)
        {
            value = Range<int>.Empty;
            return false;
        }

        switch (src.RawValue)
        {
            case Range<int> range:
                value = range;
                return true;
            case string text:
                try
                {
                    value = Range<int>.Parse(text);
                    return true;
                }
                catch
                {
                    value = Range<int>.Empty;
                    return false;
                }
            default:
                // NpgsqlRange<T> and the other provider shapes the gateway's converter reads (TYPE-002).
                if (src.RawValue is not null &&
                    RangeConverters.Int.TryConvertFromProvider(src.RawValue, AdvancedCoercions.AnyDatabase, out var converted))
                {
                    value = converted;
                    return true;
                }

                value = Range<int>.Empty;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] Range<int> value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL Range&lt;DateTime&gt; type.
/// </summary>
internal class PostgreSqlRangeDateTimeCoercion : DbCoercion<Range<DateTime>>
{
    public override bool TryRead(in DbValue src, out Range<DateTime> value)
    {
        if (src.IsNull)
        {
            value = Range<DateTime>.Empty;
            return false;
        }

        switch (src.RawValue)
        {
            case Range<DateTime> range:
                value = range;
                return true;
            case string text:
                try
                {
                    value = Range<DateTime>.Parse(text);
                    return true;
                }
                catch
                {
                    value = Range<DateTime>.Empty;
                    return false;
                }
            default:
                // NpgsqlRange<T> and the other provider shapes the gateway's converter reads (TYPE-002).
                if (src.RawValue is not null &&
                    RangeConverters.DateTime.TryConvertFromProvider(src.RawValue, AdvancedCoercions.AnyDatabase, out var converted))
                {
                    value = converted;
                    return true;
                }

                value = Range<DateTime>.Empty;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] Range<DateTime> value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL Range&lt;long&gt; type.
/// </summary>
internal class PostgreSqlRangeLongCoercion : DbCoercion<Range<long>>
{
    public override bool TryRead(in DbValue src, out Range<long> value)
    {
        if (src.IsNull)
        {
            value = Range<long>.Empty;
            return false;
        }

        switch (src.RawValue)
        {
            case Range<long> range:
                value = range;
                return true;
            case string text:
                try
                {
                    value = Range<long>.Parse(text);
                    return true;
                }
                catch
                {
                    value = Range<long>.Empty;
                    return false;
                }
            default:
                // NpgsqlRange<T> and the other provider shapes the gateway's converter reads (TYPE-002).
                if (src.RawValue is not null &&
                    RangeConverters.Long.TryConvertFromProvider(src.RawValue, AdvancedCoercions.AnyDatabase, out var converted))
                {
                    value = converted;
                    return true;
                }

                value = Range<long>.Empty;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] Range<long> value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for RowVersion value object (wraps byte[8] for SQL Server rowversion).
/// </summary>
internal class RowVersionValueCoercion : DbCoercion<RowVersion>
{
    public override bool TryRead(in DbValue src, out RowVersion value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        switch (src.RawValue)
        {
            case RowVersion rv:
                value = rv;
                return true;
            case byte[] bytes when bytes.Length == 8:
                value = new RowVersion(bytes);
                return true;
            case ulong ul:
                var byteArray = BitConverter.GetBytes(ul);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(byteArray);
                }

                value = new RowVersion(byteArray);
                return true;
            default:
                value = default;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] RowVersion value, DbParameter parameter)
    {
        parameter.Value = value.ToArray();
        parameter.DbType = DbType.Binary;
        parameter.Size = 8;
        return true;
    }
}

/// <summary>
/// Coercion for BLOB/binary large object as Stream.
/// </summary>
internal class BlobStreamCoercion : DbCoercion<Stream>
{
    public override bool TryRead(in DbValue src, out Stream value)
    {
        if (src.IsNull)
        {
            value = default!;
            return false;
        }

        switch (src.RawValue)
        {
            case Stream stream:
                if (stream.CanSeek)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                }

                // DuckDB's reader-owned UnmanagedMemoryStream must not escape the reader.
                value = ProviderStreamMaterializer.Materialize(stream);
                return true;
            case byte[] bytes:
                value = new MemoryStream(bytes, false);
                return true;
            case ReadOnlyMemory<byte> memory:
                value = new MemoryStream(memory.ToArray(), false);
                return true;
            default:
                value = default!;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] Stream value, DbParameter parameter)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Binary;
            return true;
        }

        if (value.CanSeek)
        {
            value.Seek(0, SeekOrigin.Begin);
        }

        parameter.Value = value;
        parameter.DbType = DbType.Binary;
        return true;
    }
}

/// <summary>
/// Coercion for CLOB/character large object as TextReader.
/// </summary>
internal class ClobStreamCoercion : DbCoercion<TextReader>
{
    public override bool TryRead(in DbValue src, out TextReader value)
    {
        if (src.IsNull)
        {
            value = default!;
            return false;
        }

        switch (src.RawValue)
        {
            case TextReader reader:
                value = reader;
                return true;
            case string text:
                value = new StringReader(text);
                return true;
            case Stream stream:
                try
                {
                    value = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
                    return true;
                }
                catch
                {
                    value = default!;
                    return false;
                }
            default:
                value = default!;
                return false;
        }
    }

    public override bool TryWrite([AllowNull] TextReader value, DbParameter parameter)
    {
        parameter.Value = value;
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// MySQL-family GEOMETRY: the server's internal format, a 4-byte little-endian SRID followed by
/// standard WKB, on both read and write (TYPE-018).
/// </summary>
internal static class MySqlSpatialFormat
{
    public static bool TrySplit(object? raw, out int srid, out byte[] wkb)
    {
        if (raw is byte[] bytes && bytes.Length > 4)
        {
            srid = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
            wkb = bytes.AsSpan(4).ToArray();
            return true;
        }

        srid = 0;
        wkb = Array.Empty<byte>();
        return false;
    }

    public static byte[] Join(SpatialValue value)
    {
        byte[] wkb;
        if (!value.WellKnownBinary.IsEmpty)
        {
            GeometryConverter.ExtractSridFromEwkb(value.WellKnownBinary.Span, out _, out wkb);
        }
        else if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            wkb = WellKnownTextEncoder.Encode(value.WellKnownText);
        }
        else
        {
            throw new NotSupportedException(
                "MySQL spatial values need WKB or WKT; a GeoJSON-only value cannot be written. " +
                "Create it with FromWellKnownText or FromWellKnownBinary.");
        }

        var internalFormat = new byte[4 + wkb.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(internalFormat, value.Srid);
        wkb.CopyTo(internalFormat, 4);
        return internalFormat;
    }
}

internal sealed class MySqlGeometryCoercion : DbCoercion<Geometry>
{
    private static readonly GeometryCoercion Fallback = new();

    public override bool TryRead(in DbValue src, out Geometry value)
    {
        if (MySqlSpatialFormat.TrySplit(src.RawValue, out var srid, out var wkb))
        {
            value = Geometry.FromWellKnownBinary(wkb, srid);
            return true;
        }

        return Fallback.TryRead(src, out value);
    }

    public override bool TryWrite([AllowNull] Geometry value, DbParameter parameter)
    {
        parameter.DbType = DbType.Binary;
        parameter.Value = value is null ? DBNull.Value : MySqlSpatialFormat.Join(value);
        return true;
    }
}

internal sealed class MySqlGeographyCoercion : DbCoercion<Geography>
{
    private static readonly GeographyCoercion Fallback = new();

    public override bool TryRead(in DbValue src, out Geography value)
    {
        if (MySqlSpatialFormat.TrySplit(src.RawValue, out var srid, out var wkb))
        {
            value = Geography.FromWellKnownBinary(wkb, srid);
            return true;
        }

        return Fallback.TryRead(src, out value);
    }

    public override bool TryWrite([AllowNull] Geography value, DbParameter parameter)
    {
        parameter.DbType = DbType.Binary;
        parameter.Value = value is null ? DBNull.Value : MySqlSpatialFormat.Join(value);
        return true;
    }
}

internal static class RangeConverters
{
    public static readonly PostgreSqlRangeConverter<int> Int = new();
    public static readonly PostgreSqlRangeConverter<long> Long = new();
    public static readonly PostgreSqlRangeConverter<DateTime> DateTime = new();
}

/// <summary>
/// SingleStore VECTOR(n) (F32): read as packed little-endian float32 bytes (confirmed live); written
/// as JSON array text (SqlDialect.BindsVectorsAsText).
/// </summary>
internal sealed class PackedFloat32VectorCoercion : DbCoercion<float[]>
{
    public override bool TryRead(in DbValue src, out float[] value)
    {
        if (src.RawValue is byte[] bytes && bytes.Length % 4 == 0)
        {
            value = new float[bytes.Length / 4];
            for (var i = 0; i < value.Length; i++)
            {
                value[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4 * i));
            }

            return true;
        }

        value = null!;
        return false;
    }
}
