// =============================================================================
// FILE: OffsetTimestampFieldReader.cs
// PURPOSE: Reads an offset timestamp column the provider reports as DateTime into a DateTimeOffset.
//
// AI SUMMARY:
// - Snowflake.Data reports TIMESTAMP_LTZ/TZ as DateTime but returns the DateTimeOffset from GetValue
//   (TYPE-002); ODP.NET 23 does the same for TIMESTAMP WITH TIME ZONE.
// - ODP.NET 21 (3.21) returns TIMESTAMP WITH TIME ZONE from GetValue as its wall time with no offset;
//   its GetDateTimeOffset(int) returns the value and throws for every other column type (confirmed
//   live, Oracle 23ai). The dialect names that column type (SqlDialect.OffsetTimestampDataTypeName),
//   and only a value read as DateTime from a column of that type goes through GetDateTimeOffset.
// - The provider method is found once per reader type by reflection (on the provider's reader, under
//   a tracked reader): the core library doesn't reference any provider.
// - Used by entity hydration (CompiledMapperFactory, DataReaderMapper) for DateTimeOffset properties
//   on columns the provider reports as DateTime.
// =============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace pengdows.crud.@internal;

internal static class OffsetTimestampFieldReader
{
    private static readonly ConcurrentDictionary<Type, Func<IDataRecord, int, DateTimeOffset>?> Getters = new();

    internal static readonly MethodInfo ReadMethod = typeof(OffsetTimestampFieldReader).GetMethod(nameof(Read))!;

    /// <summary>
    /// The column's value, or its DateTimeOffset when the value came back as DateTime from a column
    /// of <paramref name="offsetDataTypeName"/> and the provider can read the offset.
    /// </summary>
    public static object Read(IDataRecord record, int ordinal, string? offsetDataTypeName)
    {
        var value = record.GetValue(ordinal);
        if (value is DateTime && offsetDataTypeName != null &&
            string.Equals(record.GetDataTypeName(ordinal), offsetDataTypeName, StringComparison.Ordinal))
        {
            // Gateway hydration reads through the tracked reader; the method is on the provider's.
            IDataRecord provider = record is IInternalTrackedReader tracked ? tracked.InnerReader : record;
            if (Getters.GetOrAdd(provider.GetType(), static t => FindGetter(t)) is { } getter)
            {
                return getter(provider, ordinal);
            }
        }

        return value;
    }

    private static Func<IDataRecord, int, DateTimeOffset>? FindGetter(Type readerType)
    {
        var method = readerType.GetMethod("GetDateTimeOffset", BindingFlags.Public | BindingFlags.Instance,
            [typeof(int)]);
        if (method == null || method.ReturnType != typeof(DateTimeOffset))
        {
            return null;
        }

        var record = Expression.Parameter(typeof(IDataRecord), "record");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        return Expression.Lambda<Func<IDataRecord, int, DateTimeOffset>>(
            Expression.Call(Expression.Convert(record, readerType), method, ordinal), record, ordinal).Compile();
    }
}
