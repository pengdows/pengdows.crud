// =============================================================================
// FILE: NpgsqlTypedFieldReader.cs
// PURPOSE: Reads a PostgreSQL-family column through Npgsql's own value type, where GetValue() loses
//          information.
//
// AI SUMMARY:
// - Inet: GetValue() returns a plain IPAddress for inet, dropping the netmask ('192.168.1.10/24'::inet
//   reads as 192.168.1.10, confirmed live on Npgsql 9; TYPE-002); reads NpgsqlTypes.NpgsqlInet.
// - Interval: GetValue() returns TimeSpan for interval: positive months throw, negative months are
//   silently dropped and the days/time split is renormalized; reads NpgsqlTypes.NpgsqlInterval.
// - Read(record, ordinal) uses GetFieldValue<that type> for a column of the given data type on an
//   Npgsql reader, otherwise record.GetValue(ordinal). Npgsql is resolved by name (no dependency);
//   the GetFieldValue<T> call is compiled once into a delegate.
// - Used by entity hydration (CompiledMapperFactory, DataReaderMapper) only for Inet and
//   PostgreSqlInterval targets; raw ITrackedReader.GetValue is unchanged.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Linq.Expressions;

namespace pengdows.crud.@internal;

internal sealed class NpgsqlTypedFieldReader
{
    public static readonly NpgsqlTypedFieldReader Inet = new("NpgsqlTypes.NpgsqlInet, Npgsql", "inet");
    public static readonly NpgsqlTypedFieldReader Interval = new("NpgsqlTypes.NpgsqlInterval, Npgsql", "interval");

    private readonly string _dataTypeName;
    private readonly Lazy<Func<DbDataReader, int, object>?> _read;

    private NpgsqlTypedFieldReader(string npgsqlTypeName, string dataTypeName)
    {
        _dataTypeName = dataTypeName;
        _read = new Lazy<Func<DbDataReader, int, object>?>(() => Compile(npgsqlTypeName));
    }

    public object Read(IDataRecord record, int ordinal)
    {
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        var read = _read.Value;
        if (reader != null && read != null &&
            string.Equals(reader.GetDataTypeName(ordinal), _dataTypeName, StringComparison.OrdinalIgnoreCase))
        {
            return read(reader, ordinal);
        }

        return record.GetValue(ordinal);
    }

    private static Func<DbDataReader, int, object>? Compile(string npgsqlTypeName)
    {
        var valueType = Type.GetType(npgsqlTypeName, throwOnError: false);
        if (valueType == null)
        {
            return null;
        }

        var getFieldValue = ReaderGetters.GetFieldValueOf(valueType);
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var body = Expression.Convert(Expression.Call(reader, getFieldValue, ordinal), typeof(object));
        return Expression.Lambda<Func<DbDataReader, int, object>>(body, reader, ordinal).Compile();
    }
}
