using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Basic type coercions for common database types.
/// </summary>
public static class BasicCoercions
{
    public static void RegisterAll(CoercionRegistry registry)
    {
        registry.Register(new GuidCoercion());
        registry.Register(new RowVersionCoercion());
        registry.Register(new DateTimeOffsetCoercion());
        registry.Register(new TimeSpanCoercion());
        registry.Register(new IntArrayCoercion());
        registry.Register(new StringArrayCoercion());
        registry.Register(new JsonValueCoercion());
        registry.Register(new HStoreCoercion());
        registry.Register(new IntRangeCoercion());
        registry.Register(new DateTimeRangeCoercion());
    }
}

/// <summary>
/// Coercion for GUID values - handles Guid, byte[], and string formats.
/// </summary>
public class GuidCoercion : DbCoercion<Guid>
{
    public override bool TryRead(in DbValue src, out Guid value)
    {
        if (src.IsNull)
        {
            value = Guid.Empty;
            return false;
        }

        switch (src.RawValue)
        {
            case Guid g:
                value = g;
                return true;
            case byte[] bytes when bytes.Length == 16:
                value = new Guid(bytes);
                return true;
            case string str when Guid.TryParse(str, out var parsed):
                value = parsed;
                return true;
            default:
                value = Guid.Empty;
                return false;
        }
    }

    public override bool TryWrite(Guid value, DbParameter parameter)
    {
        parameter.Value = value;
        parameter.DbType = DbType.Guid;
        return true;
    }
}

/// <summary>
/// Coercion for SQL Server rowversion/timestamp - handles byte[] and ulong.
/// Only accepts 8-byte arrays for rowversion.
/// </summary>
public class RowVersionCoercion : DbCoercion<byte[]>
{
    public override bool TryRead(in DbValue src, out byte[]? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        switch (src.RawValue)
        {
            case byte[] bytes when bytes.Length == 8:
                value = bytes;
                return true;
            case ulong ul:
                value = BitConverter.GetBytes(ul);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(value);
                return true;
            default:
                value = null;
                return false;
        }
    }

    public override bool TryWrite(byte[]? value, DbParameter parameter)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            return true;
        }

        parameter.Value = value;
        parameter.DbType = DbType.Binary;
        return true;
    }
}

/// <summary>
/// Coercion for DateTimeOffset - handles DateTimeOffset and DateTime.
/// </summary>
public class DateTimeOffsetCoercion : DbCoercion<DateTimeOffset>
{
    public override bool TryRead(in DbValue src, out DateTimeOffset value)
    {
        if (src.IsNull)
        {
            value = DateTimeOffset.MinValue;
            return false;
        }

        switch (src.RawValue)
        {
            case DateTimeOffset dto:
                value = dto;
                return true;
            case DateTime dt:
                value = new DateTimeOffset(dt);
                return true;
            default:
                value = DateTimeOffset.MinValue;
                return false;
        }
    }

    public override bool TryWrite(DateTimeOffset value, DbParameter parameter)
    {
        parameter.Value = value;
        parameter.DbType = DbType.DateTimeOffset;
        return true;
    }
}

/// <summary>
/// Coercion for TimeSpan - handles TimeSpan, double (seconds), and time strings.
/// </summary>
public class TimeSpanCoercion : DbCoercion<TimeSpan>
{
    public override bool TryRead(in DbValue src, out TimeSpan value)
    {
        if (src.IsNull)
        {
            value = TimeSpan.Zero;
            return false;
        }

        switch (src.RawValue)
        {
            case TimeSpan ts:
                value = ts;
                return true;
            case double d:
                value = TimeSpan.FromSeconds(d);
                return true;
            case string str when TimeSpan.TryParse(str, out var parsed):
                value = parsed;
                return true;
            default:
                value = TimeSpan.Zero;
                return false;
        }
    }

    public override bool TryWrite(TimeSpan value, DbParameter parameter)
    {
        parameter.Value = value;
        parameter.DbType = DbType.Time;
        return true;
    }
}

/// <summary>
/// Coercion for int arrays - handles int[] and comma-separated strings.
/// </summary>
public class IntArrayCoercion : DbCoercion<int[]>
{
    public override bool TryRead(in DbValue src, out int[]? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        switch (src.RawValue)
        {
            case int[] arr:
                value = arr;
                return true;
            case string str:
                try
                {
                    value = str.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.Parse(s.Trim()))
                        .ToArray();
                    return true;
                }
                catch
                {
                    value = null;
                    return false;
                }
            default:
                value = null;
                return false;
        }
    }

    public override bool TryWrite(int[]? value, DbParameter parameter)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            return true;
        }

        parameter.Value = value;
        return true;
    }
}

/// <summary>
/// Coercion for string arrays - handles string[].
/// </summary>
public class StringArrayCoercion : DbCoercion<string[]>
{
    public override bool TryRead(in DbValue src, out string[]? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        if (src.RawValue is string[] arr)
        {
            value = arr;
            return true;
        }

        value = null;
        return false;
    }

    public override bool TryWrite(string[]? value, DbParameter parameter)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            return true;
        }

        parameter.Value = value;
        return true;
    }
}

/// <summary>
/// Coercion for JSON values - handles JSON strings and JsonDocument.
/// </summary>
public class JsonValueCoercion : DbCoercion<JsonValue>
{
    public override bool TryRead(in DbValue src, out JsonValue value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        switch (src.RawValue)
        {
            case JsonDocument doc:
                value = new JsonValue(doc);
                return true;
            case JsonElement element:
                value = new JsonValue(element);
                return true;
            case string str:
                try
                {
                    value = JsonValue.Parse(str);
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

    public override bool TryWrite(JsonValue value, DbParameter parameter)
    {
        parameter.Value = value.AsString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for PostgreSQL HSTORE - handles key-value pairs.
/// </summary>
public class HStoreCoercion : DbCoercion<HStore>
{
    public override bool TryRead(in DbValue src, out HStore value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        if (src.RawValue is string str)
        {
            try
            {
                value = HStore.Parse(str);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        value = default;
        return false;
    }

    public override bool TryWrite(HStore value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for integer ranges - handles PostgreSQL int4range, int8range.
/// </summary>
public class IntRangeCoercion : DbCoercion<Range<int>>
{
    public override bool TryRead(in DbValue src, out Range<int> value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        if (src.RawValue is string str)
        {
            try
            {
                value = Range<int>.Parse(str);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        value = default;
        return false;
    }

    public override bool TryWrite(Range<int> value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for DateTime ranges - handles PostgreSQL tsrange, tstzrange.
/// </summary>
public class DateTimeRangeCoercion : DbCoercion<Range<DateTime>>
{
    public override bool TryRead(in DbValue src, out Range<DateTime> value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        if (src.RawValue is string str)
        {
            try
            {
                value = Range<DateTime>.Parse(str);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        value = default;
        return false;
    }

    public override bool TryWrite(Range<DateTime> value, DbParameter parameter)
    {
        parameter.Value = value.ToString();
        parameter.DbType = DbType.String;
        return true;
    }
}
