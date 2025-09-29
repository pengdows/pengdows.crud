using System;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Simplified coercions that compile and work correctly.
/// These can be extended with more sophisticated logic later.
/// </summary>
public static class SimpleCoercions
{
    public static void RegisterAll(CoercionRegistry registry)
    {
        registry.Register(new SimpleGuidCoercion());
        registry.Register(new SimpleJsonValueCoercion());
        registry.Register(new SimpleByteArrayCoercion());
        registry.Register(new SimpleTimeSpanCoercion());
        registry.Register(new SimpleDateTimeOffsetCoercion());
    }
}

public class SimpleGuidCoercion : DbCoercion<Guid>
{
    public override bool TryRead(in DbValue src, out Guid? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        if (src.RawValue is Guid guid)
        {
            value = guid;
            return true;
        }

        if (src.RawValue is string str && Guid.TryParse(str, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (src.RawValue is byte[] bytes && bytes.Length == 16)
        {
            value = new Guid(bytes);
            return true;
        }

        value = null;
        return false;
    }

    public override bool TryWrite(Guid? value, DbParameter parameter)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value;
            parameter.DbType = DbType.Guid;
        }
        else
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Guid;
        }
        return true;
    }
}

public class SimpleJsonValueCoercion : DbCoercion<JsonValue>
{
    public override bool TryRead(in DbValue src, out JsonValue? value)
    {
        if (src.IsNull)
        {
            value = new JsonValue("null");
            return true;
        }

        if (src.RawValue is string jsonText)
        {
            try
            {
                value = JsonValue.Parse(jsonText);
                return true;
            }
            catch (JsonException)
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    public override bool TryWrite(JsonValue? value, DbParameter parameter)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value.AsString();
        }
        else
        {
            parameter.Value = DBNull.Value;
        }
        parameter.DbType = DbType.String;
        return true;
    }
}

public class SimpleByteArrayCoercion : DbCoercion<byte[]>
{
    public override bool TryRead(in DbValue src, out byte[]? value)
    {
        if (src.IsNull)
        {
            value = null;
            return true;
        }

        if (src.RawValue is byte[] bytes)
        {
            value = bytes;
            return true;
        }

        value = null;
        return false;
    }

    public override bool TryWrite(byte[]? value, DbParameter parameter)
    {
        if (value != null)
        {
            parameter.Value = value;
            parameter.DbType = DbType.Binary;
        }
        else
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Binary;
        }
        return true;
    }
}

public class SimpleTimeSpanCoercion : DbCoercion<TimeSpan>
{
    public override bool TryRead(in DbValue src, out TimeSpan? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        if (src.RawValue is TimeSpan ts)
        {
            value = ts;
            return true;
        }

        if (src.RawValue is string timeText && TimeSpan.TryParse(timeText, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    public override bool TryWrite(TimeSpan? value, DbParameter parameter)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value;
            parameter.DbType = DbType.Time;
        }
        else
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.Time;
        }
        return true;
    }
}

public class SimpleDateTimeOffsetCoercion : DbCoercion<DateTimeOffset>
{
    public override bool TryRead(in DbValue src, out DateTimeOffset? value)
    {
        if (src.IsNull)
        {
            value = null;
            return false;
        }

        if (src.RawValue is DateTimeOffset dto)
        {
            value = dto;
            return true;
        }

        if (src.RawValue is DateTime dt)
        {
            value = new DateTimeOffset(dt);
            return true;
        }

        if (src.RawValue is string dateText && DateTimeOffset.TryParse(dateText, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    public override bool TryWrite(DateTimeOffset? value, DbParameter parameter)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value;
            parameter.DbType = DbType.DateTimeOffset;
        }
        else
        {
            parameter.Value = DBNull.Value;
            parameter.DbType = DbType.DateTimeOffset;
        }
        return true;
    }
}