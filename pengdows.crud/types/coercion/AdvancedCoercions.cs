using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Advanced type coercions for database-specific types.
/// Handles spatial, network, temporal, and large object types.
/// NOTE: Many advanced types are still handled by legacy AdvancedTypeRegistry converters.
/// These coercions will be added incrementally as value objects APIs are stabilized.
/// </summary>
public static class AdvancedCoercions
{
    public static void RegisterAll(CoercionRegistry registry)
    {
        // Temporal types (have proper APIs)
        registry.Register(new PostgreSqlIntervalCoercion());

        // Value object types (have proper APIs)
        registry.Register(new RowVersionValueCoercion());

        // TODO: Add spatial, network, and other interval types once their APIs are confirmed
        // For now, these are handled by legacy AdvancedTypeRegistry converters
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

        switch (src.RawValue)
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

    public override bool TryWrite(PostgreSqlInterval value, DbParameter parameter)
    {
        parameter.Value = value.ToTimeSpan();
        parameter.DbType = DbType.Object;
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

    public override bool TryWrite(RowVersion value, DbParameter parameter)
    {
        parameter.Value = value.ToArray();
        parameter.DbType = DbType.Binary;
        parameter.Size = 8;
        return true;
    }
}
