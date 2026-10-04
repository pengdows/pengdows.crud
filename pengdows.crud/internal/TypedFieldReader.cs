using System.Data;
using System.Data.Common;
using pengdows.crud.wrappers;

namespace pengdows.crud.@internal;

/// <summary>
/// Reads the value types IDataRecord has no getter for (DateTimeOffset, TimeSpan, DateOnly, TimeOnly,
/// char, the unsigned integers) through the provider's GetFieldValue&lt;T&gt;, so the hydration of
/// each row doesn't box the value only to unbox it (PERF-020).
/// </summary>
internal static class TypedFieldReader
{
    public static bool Handles(Type type) =>
        type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(DateOnly) ||
        type == typeof(TimeOnly) || type == typeof(char) || type == typeof(sbyte) ||
        type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong);

    public static T Read<T>(IDataRecord record, int ordinal) => record switch
    {
        TrackedReader tracked => tracked.ReadFieldValue<T>(ordinal),
        DbDataReader reader => reader.GetFieldValue<T>(ordinal),
        _ => (T)record.GetValue(ordinal)
    };
}
