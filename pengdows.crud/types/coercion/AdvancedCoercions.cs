using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Advanced type coercions for database-specific types.
/// Handles spatial, network, temporal, and large object types.
/// </summary>
public static class AdvancedCoercions
{
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

        // Range types (generic)
        registry.Register(new PostgreSqlRangeIntCoercion());
        registry.Register(new PostgreSqlRangeDateTimeCoercion());
        registry.Register(new PostgreSqlRangeLongCoercion());

        // Concurrency/versioning
        registry.Register(new RowVersionValueCoercion());

        // Large object types (LOBs)
        registry.Register(new BlobStreamCoercion());
        registry.Register(new ClobStreamCoercion());
    }
}

/// <summary>
/// Coercion for PostgreSQL INTERVAL type.
/// </summary>
public class PostgreSqlIntervalCoercion : DbCoercion<PostgreSqlInterval>
{
    public override bool TryRead(in DbValue src, out PostgreSqlInterval value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }
        if (src.RawValue is null)
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
            default:
                value = default;
                return false;
        }
    }

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
public class IntervalYearMonthCoercion : DbCoercion<IntervalYearMonth>
{
    public override bool TryRead(in DbValue src, out IntervalYearMonth value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }
        if (src.RawValue is null)
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
public class IntervalDaySecondCoercion : DbCoercion<IntervalDaySecond>
{
    public override bool TryRead(in DbValue src, out IntervalDaySecond value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }
        if (src.RawValue is null)
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
public class InetCoercion : DbCoercion<Inet>
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
                        value = new Inet(addr, prefix);
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
/// Coercion for PostgreSQL CIDR type (network address).
/// </summary>
public class CidrCoercion : DbCoercion<Cidr>
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
public class MacAddressCoercion : DbCoercion<MacAddress>
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
public class GeometryCoercion : DbCoercion<Geometry>
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
                    value = Geometry.FromWellKnownBinary(bytes, 0);
                    return true;
                case string text when text.StartsWith("{"):
                    value = Geometry.FromGeoJson(text, 0);
                    return true;
                case string text:
                    value = Geometry.FromWellKnownText(text, 0);
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
            parameter.Value = value.WellKnownBinary.ToArray();
            parameter.DbType = DbType.Binary;
        }
        else
        {
            parameter.Value = value.WellKnownText;
            parameter.DbType = DbType.String;
        }
        return true;
    }
}

/// <summary>
/// Coercion for spatial GEOGRAPHY type.
/// </summary>
public class GeographyCoercion : DbCoercion<Geography>
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
                    value = Geography.FromWellKnownBinary(bytes, 4326);
                    return true;
                case string text when text.StartsWith("{"):
                    value = Geography.FromGeoJson(text, 4326);
                    return true;
                case string text:
                    value = Geography.FromWellKnownText(text, 4326);
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
            parameter.Value = value.WellKnownBinary.ToArray();
            parameter.DbType = DbType.Binary;
        }
        else
        {
            parameter.Value = value.WellKnownText;
            parameter.DbType = DbType.String;
        }
        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL Range&lt;int&gt; type.
/// </summary>
public class PostgreSqlRangeIntCoercion : DbCoercion<Range<int>>
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
public class PostgreSqlRangeDateTimeCoercion : DbCoercion<Range<DateTime>>
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
public class PostgreSqlRangeLongCoercion : DbCoercion<Range<long>>
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
public class RowVersionValueCoercion : DbCoercion<RowVersion>
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
                    Array.Reverse(byteArray);
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
public class BlobStreamCoercion : DbCoercion<Stream>
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
                    stream.Seek(0, SeekOrigin.Begin);
                value = stream;
                return true;
            case byte[] bytes:
                value = new MemoryStream(bytes, writable: false);
                return true;
            case ReadOnlyMemory<byte> memory:
                value = new MemoryStream(memory.ToArray(), writable: false);
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
            value.Seek(0, SeekOrigin.Begin);

        parameter.Value = value;
        parameter.DbType = DbType.Binary;
        return true;
    }
}

/// <summary>
/// Coercion for CLOB/character large object as TextReader.
/// </summary>
public class ClobStreamCoercion : DbCoercion<TextReader>
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
                    value = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
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
