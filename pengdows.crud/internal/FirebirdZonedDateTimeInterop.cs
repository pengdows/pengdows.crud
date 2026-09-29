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

    private static readonly Lazy<Type?> ByName = new(() => Type.GetType(AssemblyQualifiedTypeName, throwOnError: false));

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
        return value != null && string.Equals(value.GetType().FullName, TypeName, StringComparison.Ordinal);
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
