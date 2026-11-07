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
        if (ctor is null)
        {
            return null;
        }

        // Firebird stores the UTC time with the zone. The zone is the Etc/GMT zone of the offset, so the offset is kept; an
        // offset that is not whole hours has no such zone and is refused rather than sent as UTC.
        var zone = ZoneNameFor(value.Offset) ?? throw new NotSupportedException(
            $"Firebird TIME WITH TIME ZONE takes whole-hour offsets only; {value.Offset} has no named zone.");
        return ctor.Invoke(new object[] { value.UtcDateTime.TimeOfDay, zone });
    }

    /// <summary>
    /// The Etc/GMT zone name for a whole-hour offset ("UTC" for zero; note the sign is the inverse: +02:00 is Etc/GMT-2), or
    /// null for an offset that is not whole hours or is outside ±14.
    /// </summary>
    internal static string? ZoneNameFor(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            return "UTC";
        }

        if (offset.Minutes != 0 || offset.Seconds != 0 || offset.Milliseconds != 0 || Math.Abs(offset.TotalHours) > 14)
        {
            return null;
        }

        var hours = Math.Abs((int)offset.TotalHours);
        return offset > TimeSpan.Zero ? $"Etc/GMT-{hours}" : $"Etc/GMT+{hours}";
    }

    /// <summary>The offset a zone name stands for: "UTC" is zero, Etc/GMT±h is the inverse-signed h hours; otherwise null.</summary>
    internal static TimeSpan? OffsetOfZoneName(string? zone)
    {
        if (zone == "UTC")
        {
            return TimeSpan.Zero;
        }

        var match = System.Text.RegularExpressions.Regex.Match(zone ?? string.Empty, @"^Etc/GMT([+-])(\d{1,2})$");
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return match.Groups[1].Value == "-" ? TimeSpan.FromHours(hours) : TimeSpan.FromHours(-hours);
    }

    /// <summary>
    /// Reads an FbZonedTime (returned for TIME WITH TIME ZONE columns) as a DateTimeOffset on
    /// 0001-01-01: its UTC time, expressed at its offset when the value carries one.
    /// </summary>
    internal static bool TryGetTime(object value, out DateTimeOffset time)
    {
        var type = value.GetType();
        if (!ProviderTypeNames.IsFirebirdZonedTime(type)
            || type.GetProperty("Time")?.GetValue(value) is not TimeSpan utcTime)
        {
            time = default;
            return false;
        }

        // The stated offset when the driver carries one; otherwise the offset the zone name stands for (a read from the server
        // carries the zone); otherwise UTC.
        var stated = type.GetProperty("Offset")?.GetValue(value) as TimeSpan?;
        var zone = type.GetProperty("TimeZone")?.GetValue(value) as string;
        var offset = stated is { } s && s != TimeSpan.Zero ? s : OffsetOfZoneName(zone) ?? TimeSpan.Zero;
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
        return value != null && (ProviderTypeNames.IsFirebirdZonedDateTime(value.GetType()) || ProviderTypeNames.IsFirebirdZonedTime(value.GetType()));
    }

    /// <summary>
    /// <see cref="IsZoned(object?)"/> for a generic value, without boxing it and without comparing type names when the answer is
    /// fixed by <typeparamref name="T"/>: decided once per T for a value type or sealed class, from the value itself only when T
    /// is not exact (an object, an interface).
    /// </summary>
    internal static bool IsZoned<T>(T value) =>
        ZonedType<T>.IsExact ? ZonedType<T>.IsZonedType : IsZoned((object?)value);

    private static class ZonedType<T>
    {
        internal static readonly bool IsExact = typeof(T).IsValueType || typeof(T).IsSealed;

        internal static readonly bool IsZonedType = ProviderTypeNames.IsFirebirdZonedDateTime(typeof(T)) || ProviderTypeNames.IsFirebirdZonedTime(typeof(T));
    }

    /// <summary>
    /// Reads an FbZonedDateTime as a DateTimeOffset: its UTC instant, expressed at its offset when
    /// the value carries one.
    /// </summary>
    internal static bool TryGetInstant(object value, out DateTimeOffset instant)
    {
        var type = value.GetType();
        if (!ProviderTypeNames.IsFirebirdZonedDateTime(type)
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
