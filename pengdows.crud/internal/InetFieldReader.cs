// =============================================================================
// FILE: InetFieldReader.cs
// PURPOSE: Reads PostgreSQL-family inet columns without losing the netmask.
//
// AI SUMMARY:
// - Npgsql's GetValue() returns a plain IPAddress for inet columns, dropping the netmask
//   ('192.168.1.10/24'::inet reads as 192.168.1.10, confirmed live on Npgsql 9; TYPE-002).
// - Read(record, ordinal) returns a boxed NpgsqlTypes.NpgsqlInet (address + netmask) for an
//   "inet" column on an Npgsql reader, otherwise record.GetValue(ordinal).
// - Npgsql is resolved by name; pengdows.crud takes no dependency on it. The GetFieldValue<T> call
//   is compiled once into a delegate.
// - Used by entity hydration (CompiledMapperFactory, DataReaderMapper) only for Inet targets;
//   raw ITrackedReader.GetValue is unchanged.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Linq.Expressions;

namespace pengdows.crud.@internal;

internal static class InetFieldReader
{
    private static readonly Lazy<Func<DbDataReader, int, object>?> ReadNpgsqlInet = new(Compile);

    public static object Read(IDataRecord record, int ordinal)
    {
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        var read = ReadNpgsqlInet.Value;
        if (reader != null && read != null &&
            string.Equals(reader.GetDataTypeName(ordinal), "inet", StringComparison.OrdinalIgnoreCase))
        {
            return read(reader, ordinal);
        }

        return record.GetValue(ordinal);
    }

    private static Func<DbDataReader, int, object>? Compile()
    {
        var inetType = Type.GetType("NpgsqlTypes.NpgsqlInet, Npgsql", throwOnError: false);
        if (inetType == null)
        {
            return null;
        }

        var getFieldValue = ReaderGetters.GetFieldValueOf(inetType);
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var body = Expression.Convert(Expression.Call(reader, getFieldValue, ordinal), typeof(object));
        return Expression.Lambda<Func<DbDataReader, int, object>>(body, reader, ordinal).Compile();
    }
}
