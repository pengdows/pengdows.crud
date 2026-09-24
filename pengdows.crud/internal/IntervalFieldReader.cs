// =============================================================================
// FILE: IntervalFieldReader.cs
// PURPOSE: Reads PostgreSQL-family interval columns without losing months.
//
// AI SUMMARY:
// - Npgsql's GetValue() returns TimeSpan for interval columns: positive months throw, negative
//   months are silently dropped, and the stored days/time split is renormalized.
// - Read(record, ordinal) returns a boxed NpgsqlTypes.NpgsqlInterval (months, days, microseconds)
//   for an "interval" column on an Npgsql reader, otherwise record.GetValue(ordinal).
// - Npgsql is resolved by name; pengdows.crud takes no dependency on it. The GetFieldValue<T> call
//   is compiled once into a delegate.
// - Used by entity hydration (CompiledMapperFactory, DataReaderMapper) only for PostgreSqlInterval
//   targets; raw ITrackedReader.GetValue is unchanged.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Linq.Expressions;

namespace pengdows.crud.@internal;

internal static class IntervalFieldReader
{
    private static readonly Lazy<Func<DbDataReader, int, object>?> ReadNpgsqlInterval = new(Compile);

    public static object Read(IDataRecord record, int ordinal)
    {
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        var read = ReadNpgsqlInterval.Value;
        if (reader != null && read != null &&
            string.Equals(reader.GetDataTypeName(ordinal), "interval", StringComparison.OrdinalIgnoreCase))
        {
            return read(reader, ordinal);
        }

        return record.GetValue(ordinal);
    }

    private static Func<DbDataReader, int, object>? Compile()
    {
        var intervalType = Type.GetType("NpgsqlTypes.NpgsqlInterval, Npgsql", throwOnError: false);
        if (intervalType == null)
        {
            return null;
        }

        var getFieldValue = typeof(DbDataReader).GetMethods()
            .First(m => m.Name == nameof(DbDataReader.GetFieldValue) && m.IsGenericMethodDefinition)
            .MakeGenericMethod(intervalType);
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var body = Expression.Convert(Expression.Call(reader, getFieldValue, ordinal), typeof(object));
        return Expression.Lambda<Func<DbDataReader, int, object>>(body, reader, ordinal).Compile();
    }
}
