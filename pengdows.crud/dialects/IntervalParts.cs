// =============================================================================
// FILE: IntervalParts.cs
// PURPOSE: Converts a provider's months/days/microseconds interval value to a TimeSpan exactly.
//
// AI SUMMARY:
// - ToTimeSpan(): reads Months, Days and Micros (any integer type; a ulong is reinterpreted as signed,
//   as DuckDB.NET's DuckDBInterval stores it) by reflection, cached per type; no package reference.
// - A non-zero month count throws InvalidCastException: a month has no fixed length.
// =============================================================================

using System.Collections.Concurrent;
using System.Reflection;

namespace pengdows.crud.dialects;

internal static class IntervalParts
{
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Months, PropertyInfo Days, PropertyInfo Micros)> Members = new();

    public static TimeSpan ToTimeSpan(object interval)
    {
        if (interval is TimeSpan span)
        {
            return span;
        }

        var (monthsProperty, daysProperty, microsProperty) = Members.GetOrAdd(interval.GetType(), static type =>
            (Property(type, "Months"), Property(type, "Days"), Property(type, "Micros")));
        var months = Convert.ToInt64(monthsProperty.GetValue(interval));
        if (months != 0)
        {
            throw new InvalidCastException(
                $"An interval of {months} month(s) can't be read as a TimeSpan: a month has no fixed length.");
        }

        var days = Convert.ToInt64(daysProperty.GetValue(interval));
        var micros = microsProperty.GetValue(interval) switch
        {
            ulong unsigned => unchecked((long)unsigned),
            var other => Convert.ToInt64(other)
        };
        return TimeSpan.FromTicks(checked((days * 86_400_000_000L + micros) * 10));
    }

    private static PropertyInfo Property(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidCastException($"{type.FullName} has no {name}; it can't be read as an interval.");
}
