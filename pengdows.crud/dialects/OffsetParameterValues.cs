// =============================================================================
// FILE: OffsetParameterValues.cs
// PURPOSE: The DateTimeOffset a parameter was created from, where the dialect sent its UTC instant.
//
// AI SUMMARY:
// - DRY-029: Oracle and Snowflake send a DateTimeOffset parameter as its UTC instant, which every
//   column type stores correctly; a parameter can't tell which column it is going to. The type
//   mapping notes the value it was given here, and SqlDialect.MarkColumnParameter re-binds it with its
//   offset when the gateways have learned that the column keeps one (TIMESTAMP WITH TIME ZONE,
//   TIMESTAMP_TZ).
// - Weak keys: an entry lives as long as its parameter.
// =============================================================================

using System.Data.Common;
using System.Runtime.CompilerServices;

namespace pengdows.crud.dialects;

internal static class OffsetParameterValues
{
    private static readonly ConditionalWeakTable<DbParameter, StrongBox<DateTimeOffset>> Values = new();

    internal static void Remember(DbParameter parameter, DateTimeOffset value) =>
        Values.AddOrUpdate(parameter, new StrongBox<DateTimeOffset>(value));

    internal static bool TryGet(DbParameter parameter, out DateTimeOffset value)
    {
        if (Values.TryGetValue(parameter, out var box))
        {
            value = box.Value;
            return true;
        }

        value = default;
        return false;
    }
}
