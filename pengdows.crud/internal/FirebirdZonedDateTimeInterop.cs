// =============================================================================
// FILE: FirebirdZonedDateTimeInterop.cs
// PURPOSE: Reflection bridge to FirebirdClient's FbZonedDateTime (pengdows.crud does not reference
//          the Firebird driver directly).
//
// AI SUMMARY:
// - Create(): builds FbZonedDateTime(utcDateTime, "UTC") for Firebird 4+ DateTimeOffset writes.
//   CONFIRMED live (Firebird 5.0.4, FirebirdClient 10.3.3): the driver encodes it against whichever
//   column type the server describes - TIMESTAMP WITH TIME ZONE stores the instant, TIMESTAMP stores
//   the UTC wall time - so the library does not need to know the column type.
// - TryGetInstant(): reads an FbZonedDateTime (returned for TIMESTAMP WITH TIME ZONE columns) as a
//   DateTimeOffset instant.
// - The type is resolved from the provider factory's assembly, then by assembly-qualified name;
//   when it can't be found, callers fall back to the UTC-DateTime coercion.
// =============================================================================

using System.Reflection;

namespace pengdows.crud.@internal;

internal static class FirebirdZonedDateTimeInterop
{
    private const string TypeName = "FirebirdSql.Data.Types.FbZonedDateTime";
    private const string AssemblyQualifiedTypeName = TypeName + ", FirebirdSql.Data.FirebirdClient";
    private const string TimeTypeName = "FirebirdSql.Data.Types.FbZonedTime";

    private static readonly Lazy<Type?> ByName = new(() => Type.GetType(AssemblyQualifiedTypeName, throwOnError: false));
    private static readonly Lazy<Type?> TimeByName =
        new(() => Type.GetType(TimeTypeName + ", FirebirdSql.Data.FirebirdClient", throwOnError: false));

    /// <summary>
    /// Builds an FbZonedTime holding <paramref name="value"/>'s UTC time of day in zone "UTC" (for TIME
    /// WITH TIME ZONE; the driver accepts named zones only, confirmed live), or null when the driver
    /// type is unavailable.
    /// </summary>
    internal static object? CreateUtcTime(DateTimeOffset value, Assembly? providerAssembly)
    {
        var type = providerAssembly?.GetType(TimeTypeName, throwOnError: false) ?? TimeByName.Value;
        var ctor = type?.GetConstructor(new[] { typeof(TimeSpan), typeof(string) });
        return ctor?.Invoke(new object[] { value.UtcDateTime.TimeOfDay, "UTC" });
    }

    /// <summary>
    /// Reads an FbZonedTime (returned for TIME WITH TIME ZONE columns) as a DateTimeOffset on
    /// 0001-01-01: its UTC time, expressed at its offset when the value carries one.
    /// </summary>
    internal static bool TryGetTime(object value, out DateTimeOffset time)
    {
        var type = value.GetType();
        if (!string.Equals(type.FullName, TimeTypeName, StringComparison.Ordinal)
            || type.GetProperty("Time")?.GetValue(value) is not TimeSpan utcTime)
        {
            time = default;
            return false;
        }

        var offset = type.GetProperty("Offset")?.GetValue(value) is TimeSpan stated ? stated : TimeSpan.Zero;
        // Anchored on 0001-01-01 UTC; one day later only when the offset would otherwise land
        // before DateTime.MinValue, which threw for a negative offset (REV-065).
        var anchor = utcTime + offset < TimeSpan.Zero ? DateTime.MinValue.AddDays(1) : DateTime.MinValue;
        time = new DateTimeOffset(anchor.Add(utcTime), TimeSpan.Zero);
        if (offset != TimeSpan.Zero)
        {
            time = time.ToOffset(offset);
        }

        return true;
    }

    /// <summary>
    /// Builds an FbZonedDateTime holding <paramref name="value"/>'s UTC instant in zone "UTC", or
    /// null when the driver type is unavailable.
    /// </summary>
    internal static object? CreateUtc(DateTimeOffset value, Assembly? providerAssembly)
    {
        var type = providerAssembly?.GetType(TypeName, throwOnError: false) ?? ByName.Value;
        var ctor = type?.GetConstructor(new[] { typeof(DateTime), typeof(string) });
        return ctor?.Invoke(new object[] { DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Utc), "UTC" });
    }

    /// <summary>True when <paramref name="value"/> is an FbZonedDateTime.</summary>
    internal static bool IsZoned(object? value)
    {
        return value != null && value.GetType().FullName is TypeName or TimeTypeName;
    }

    /// <summary>
    /// Reads an FbZonedDateTime as a DateTimeOffset: its UTC instant, expressed at its offset when
    /// the value carries one.
    /// </summary>
    internal static bool TryGetInstant(object value, out DateTimeOffset instant)
    {
        var type = value.GetType();
        if (!string.Equals(type.FullName, TypeName, StringComparison.Ordinal)
            || type.GetProperty("DateTime")?.GetValue(value) is not DateTime utc)
        {
            instant = default;
            return false;
        }

        instant = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        if (type.GetProperty("Offset")?.GetValue(value) is TimeSpan offset)
        {
            instant = instant.ToOffset(offset);
        }

        return true;
    }
}
