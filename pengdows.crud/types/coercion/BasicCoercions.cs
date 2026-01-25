using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Basic type coercions for common database types.
/// </summary>
public static class BasicCoercions
{
    public static void RegisterAll(CoercionRegistry registry)
    {
        // Primitive types
        registry.Register(new GuidCoercion());
        registry.Register(new BooleanCoercion());
        registry.Register(new DateTimeCoercion());
        registry.Register(new DateTimeOffsetCoercion());
        registry.Register(new TimeSpanCoercion());
        registry.Register(new DecimalCoercion());

        // Binary types
        registry.Register(new ByteArrayCoercion());

        // Array types
        registry.Register(new IntArrayCoercion());
        registry.Register(new StringArrayCoercion());

        // JSON types
        registry.Register(new JsonValueCoercion());
        registry.Register(new JsonDocumentCoercion());
        registry.Register(new JsonElementCoercion());

        // PostgreSQL types
        registry.Register(new HStoreCoercion());
        registry.Register(new IntRangeCoercion());
        registry.Register(new DateTimeRangeCoercion());
    }
}

/// <summary>
/// Coercion for GUID values - handles Guid, byte[], ReadOnlyMemory, ArraySegment, char[], and string formats.
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
            case ReadOnlyMemory<byte> memory when memory.Length == 16:
                value = new Guid(memory.Span);
                return true;
            case ArraySegment<byte> segment when segment.Count == 16:
                value = new Guid(segment.AsSpan());
                return true;
            case char[] chars when chars.Length == 36 && Guid.TryParse(new string(chars), out var charGuid):
                value = charGuid;
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

/// <summary>
/// Coercion for Boolean - handles bool, string, char, and numeric representations.
/// </summary>
public class BooleanCoercion : DbCoercion<bool>
{
    public override bool TryRead(in DbValue src, out bool value)
    {
        if (src.IsNull)
        {
            value = false;
            return false;
        }

        switch (src.RawValue)
        {
            case bool b:
                value = b;
                return true;
            case string s:
                if (bool.TryParse(s, out value))
                {
                    return true;
                }

                if (s.Length == 1)
                {
                    value = EvaluateCharBoolean(char.ToLowerInvariant(s[0]));
                    return true;
                }

                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl))
                {
                    value = Math.Abs(dbl) > double.Epsilon;
                    return true;
                }

                value = false;
                return false;
            case char c:
                value = EvaluateCharBoolean(char.ToLowerInvariant(c));
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                value = Convert.ToInt64(src.RawValue, CultureInfo.InvariantCulture) != 0;
                return true;
            case float f:
                value = Math.Abs(f) > float.Epsilon;
                return true;
            case double d:
                value = Math.Abs(d) > double.Epsilon;
                return true;
            case decimal m:
                value = m != decimal.Zero;
                return true;
            default:
                value = false;
                return false;
        }
    }

    public override bool TryWrite(bool value, DbParameter parameter)
    {
        parameter.Value = value;
        parameter.DbType = DbType.Boolean;
        return true;
    }

    private static bool EvaluateCharBoolean(char lower)
    {
        return lower switch
        {
            't' or 'y' or '1' => true,
            'f' or 'n' or '0' => false,
            _ => throw new InvalidCastException($"Cannot convert character '{lower}' to Boolean.")
        };
    }
}

/// <summary>
/// Coercion for DateTime - handles DateTime, DateTimeOffset, and strings.
/// Always normalizes to UTC.
/// </summary>
public class DateTimeCoercion : DbCoercion<DateTime>
{
    public override bool TryRead(in DbValue src, out DateTime value)
    {
        if (src.IsNull)
        {
            value = DateTime.MinValue;
            return false;
        }

        switch (src.RawValue)
        {
            case DateTime dt:
                value = DateTime.SpecifyKind(ConvertToUtc(dt), DateTimeKind.Utc);
                return true;
            case DateTimeOffset dto:
                value = DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Utc);
                return true;
            case string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var dto):
                value = DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Utc);
                return true;
            case string s
                when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt):
                value = DateTime.SpecifyKind(ConvertToUtc(dt), DateTimeKind.Utc);
                return true;
            default:
                value = DateTime.MinValue;
                return false;
        }
    }

    public override bool TryWrite(DateTime value, DbParameter parameter)
    {
        parameter.Value = value;
        parameter.DbType = DbType.DateTime;
        return true;
    }

    private static DateTime ConvertToUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }
}

/// <summary>
/// Coercion for Decimal - handles all numeric conversions with culture invariance.
/// </summary>
public class DecimalCoercion : DbCoercion<decimal>
{
    public override bool TryRead(in DbValue src, out decimal value)
    {
        if (src.IsNull)
        {
            value = 0m;
            return false;
        }

        switch (src.RawValue)
        {
            case decimal d:
                value = d;
                return true;
            default:
                try
                {
                    value = Convert.ToDecimal(src.RawValue, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    value = 0m;
                    return false;
                }
        }
    }

    public override bool TryWrite(decimal value, DbParameter parameter)
    {
        parameter.Value = value;
        parameter.DbType = DbType.Decimal;
        return true;
    }
}

/// <summary>
/// Coercion for byte arrays - handles byte[], ReadOnlyMemory, ArraySegment.
/// </summary>
public class ByteArrayCoercion : DbCoercion<byte[]>
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
            case byte[] bytes:
                value = bytes;
                return true;
            case ReadOnlyMemory<byte> memory:
                value = memory.ToArray();
                return true;
            case ArraySegment<byte> segment:
                value = segment.ToArray();
                return true;
            default:
                value = null;
                return false;
        }
    }

    public override bool TryWrite(byte[]? value, DbParameter parameter)
    {
        parameter.Value = value ?? (object)DBNull.Value;
        parameter.DbType = DbType.Binary;
        return true;
    }
}

/// <summary>
/// Coercion for JsonDocument - handles JsonDocument, JsonElement, and JSON strings.
/// </summary>
public class JsonDocumentCoercion : DbCoercion<JsonDocument>
{
    public override bool TryRead(in DbValue src, out JsonDocument? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        try
        {
            switch (src.RawValue)
            {
                case JsonDocument doc:
                    value = doc;
                    return true;
                case JsonElement element:
                    value = JsonDocument.Parse(element.GetRawText());
                    return true;
                case string s when !string.IsNullOrWhiteSpace(s):
                    value = JsonDocument.Parse(s);
                    return true;
                case byte[] bytes when bytes.Length > 0:
                    value = JsonDocument.Parse(bytes);
                    return true;
                default:
                    value = null;
                    return false;
            }
        }
        catch
        {
            value = null;
            return false;
        }
    }

    public override bool TryWrite(JsonDocument? value, DbParameter parameter)
    {
        if (value == null)
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.String;
            return true;
        }

        parameter.Value = value.RootElement.GetRawText();
        parameter.DbType = DbType.String;
        return true;
    }
}

/// <summary>
/// Coercion for JsonElement - handles JsonElement, JsonDocument, and JSON strings.
/// </summary>
public class JsonElementCoercion : DbCoercion<JsonElement>
{
    public override bool TryRead(in DbValue src, out JsonElement value)
    {
        if (src.IsNull)
        {
            value = default;
            return false;
        }

        try
        {
            switch (src.RawValue)
            {
                case JsonElement element:
                    value = element;
                    return true;
                case JsonDocument doc:
                    value = doc.RootElement.Clone();
                    return true;
                case string s when !string.IsNullOrWhiteSpace(s):
                    using (var doc = JsonDocument.Parse(s))
                    {
                        value = doc.RootElement.Clone();
                        return true;
                    }
                case byte[] bytes when bytes.Length > 0:
                    using (var doc = JsonDocument.Parse(bytes))
                    {
                        value = doc.RootElement.Clone();
                        return true;
                    }
                default:
                    value = default;
                    return false;
            }
        }
        catch
        {
            value = default;
            return false;
        }
    }

    public override bool TryWrite(JsonElement value, DbParameter parameter)
    {
        parameter.Value = value.GetRawText();
        parameter.DbType = DbType.String;
        return true;
    }
}