// =============================================================================
// FILE: DateTimeCoercion.cs
// PURPOSE: Converts date and time values: a DateTime or DateTimeOffset to the other, timestamp text with or without an
//          offset, and the UTC normalisation the time policy asks for.
//
// AI SUMMARY:
// - CoerceDateTime / CoerceDateTimeOffset: the coercions a read or a write applies to a date or time value.
// - CoerceDateTimeFromString / CoerceDateTimeOffsetFromString: the parse of timestamp text (typed reads).
// - TryParseTimestampText: the one parse of timestamp text, with or without an offset (an offsetless text is UTC).
// - NormalizeDateTime / DateTimeOffsetFromDateTime / ConvertToUtc: UTC normalisation, per the time policy.
// =============================================================================

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using pengdows.crud.@internal;
using pengdows.crud.enums;

namespace pengdows.crud.types.coercion;

internal static class DateTimeCoercion
{
    // Only a provider that writes a time with zone as text reads one back from text (DatabaseTraits.TimeWithZoneIsText).
    private static bool TimeWithZoneIsText(TypeCoercionOptions options) =>
        pengdows.crud.dialects.DatabaseTraits.For(options.Provider).TimeWithZoneIsText;

    // A time of day with an offset and no date: "HH:mm[:ss[.fffffff]]" then "Z" or "+hh:mm" / "-hh:mm". It is how a
    // TIME WITH TIME ZONE value is stored as text (SQLite), and it is never a timestamp: a timestamp has a date.
    private static readonly Regex TimeWithZoneText = new(
        @"^(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?(Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant);

    internal static object CoerceDateTimeOffset(object value, TypeCoercionOptions options)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                return dto;
            case string zonedText when TimeWithZoneIsText(options) && TryParseTimeWithZoneText(zonedText, out var zonedTime):
                return zonedTime;
            case not null when FirebirdZonedDateTimeInterop.TryGetInstant(value, out var zoned):
                // Firebird TIMESTAMP WITH TIME ZONE columns are returned as FbZonedDateTime.
                return zoned;
            case not null when FirebirdZonedDateTimeInterop.TryGetTime(value, out var zonedTime):
                // Firebird TIME WITH TIME ZONE columns are returned as FbZonedTime (TYPE-002).
                return zonedTime;
            case DateTime dt:
                return DateTimeOffsetFromDateTime(dt, options);
            case string s when TryParseTimestampText(s, out var parsed):
                return parsed;
            default:
                throw new InvalidCastException("Cannot convert value to DateTimeOffset.");
        }
    }

    internal static object CoerceDateTime(object value, TypeCoercionOptions options)
    {
        switch (value)
        {
            case DateTime dt:
                return DateTime.SpecifyKind(ConvertToUtc(dt), DateTimeKind.Utc);
            case DateTimeOffset dto:
                return DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Utc);
            case not null when FirebirdZonedDateTimeInterop.TryGetInstant(value, out var zoned):
                // Firebird TIMESTAMP WITH TIME ZONE columns are returned as FbZonedDateTime.
                return DateTime.SpecifyKind(zoned.UtcDateTime, DateTimeKind.Utc);
            case string s when string.IsNullOrWhiteSpace(s):
                // Treat empty/whitespace strings as invalid for DateTime
                // This handles SQLite returning empty strings for TIMESTAMP columns
                throw new InvalidCastException("Cannot convert value to DateTime.");
            case string s when TryParseTimestampText(s, out var dto):
                return dto.UtcDateTime;
            default:
                throw new InvalidCastException("Cannot convert value to DateTime.");
        }
    }

    /// <summary>
    /// Typed DateTime parser for string source that avoids boxing the DateTime return value.
    /// Applies the same UTC normalization as <see cref="CoerceDateTime"/> for the string case.
    /// (<see cref="DataReaderMapper"/>'s compiled string→DateTime path uses
    /// <c>IDataRecord.GetDateTime</c> + <see cref="NormalizeDateTime"/> instead.)
    /// </summary>
    internal static DateTime CoerceDateTimeFromString(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            throw new InvalidCastException("Cannot convert value to DateTime.");
        }

        if (TryParseTimestampText(s, out var dto))
        {
            return dto.UtcDateTime;
        }

        throw new InvalidCastException("Cannot convert value to DateTime.");
    }

    /// <summary>
    /// Typed DateTimeOffset parser for string source — used by compiled expression trees in
    /// <see cref="DataReaderMapper"/> to avoid boxing the DateTimeOffset return value.
    /// </summary>
    internal static DateTimeOffset CoerceDateTimeOffsetFromString(string s, TypeCoercionOptions options)
    {
        if (TryParseTimestampText(s, out var parsed) || (TimeWithZoneIsText(options) && TryParseTimeWithZoneText(s, out parsed)))
        {
            return parsed;
        }

        throw new InvalidCastException("Cannot convert value to DateTimeOffset.");
    }

    /// <summary>
    /// A TIME WITH TIME ZONE value's text: its time of day at its offset, on the date 2000-01-01 (the date Npgsql reads a
    /// time with zone on). Text with no offset is not a time with zone, and an offset with a date is a timestamp.
    /// </summary>
    internal static bool TryParseTimeWithZoneText(string s, out DateTimeOffset value)
    {
        value = default;
        var match = TimeWithZoneText.Match(s.Trim());
        if (!match.Success)
        {
            return false;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        var ticks = match.Groups[4].Success
            ? long.Parse(match.Groups[4].Value.PadRight(7, '0'), CultureInfo.InvariantCulture)
            : 0L;
        if (hours > 23 || minutes > 59 || seconds > 59)
        {
            return false;
        }

        var offsetText = match.Groups[5].Value;
        TimeSpan offset = TimeSpan.Zero;
        if (offsetText != "Z")
        {
            var magnitude = TimeSpan.ParseExact(offsetText[1..], @"hh\:mm", CultureInfo.InvariantCulture);
            offset = offsetText[0] == '-' ? -magnitude : magnitude;
            if (offset < TimeSpan.FromHours(-14) || offset > TimeSpan.FromHours(14))
            {
                return false;
            }
        }

        var timeOfDay = new TimeSpan(0, hours, minutes, seconds) + TimeSpan.FromTicks(ticks);
        value = new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).Add(timeOfDay), offset);
        return true;
    }

    /// <summary>
    /// A DateTime as a DateTimeOffset under <paramref name="options"/>' time policy, as
    /// <see cref="CoerceDateTimeOffset"/> converts it, without boxing (DRY-003).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static DateTimeOffset DateTimeOffsetFromDateTime(DateTime dt, TypeCoercionOptions options) =>
        options.TimePolicy == TimeMappingPolicy.ForceUtcDateTime
            ? new DateTimeOffset(ConvertToUtc(dt), TimeSpan.Zero)
            : CreateFlexibleOffset(dt);

    /// <summary>
    /// Timestamp text: a stated offset keeps its instant; text without one is UTC, as an unspecified
    /// DateTime is everywhere else. DateTimeOffset.TryParse alone assumed the machine's local time,
    /// so the same text read differently on differently configured hosts (REV-059). One parse,
    /// where the old path tried DateTimeOffset then DateTime. Text with no date (a time of day) is not a
    /// timestamp: it took today's date, a value that depended on the day it was read.
    /// </summary>
    internal static bool TryParseTimestampText(string s, out DateTimeOffset value)
    {
        if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            return false;
        }

        // DateTimeOffset.TryParse refuses NoCurrentDateDefault, so a DateTime parse with it tells whether
        // the text had a date: a time of day comes back on 0001-01-01, which only text saying 0001 means.
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var probe) &&
            probe is { Year: 1, Month: 1, Day: 1 } && !s.Contains("0001", StringComparison.Ordinal))
        {
            value = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes a DateTime returned by a database driver to DateTimeKind.Utc.
    /// Many drivers (Snowflake, SQL Server, MySQL, Oracle) return TIMESTAMP/DATETIME columns
    /// as DateTimeKind.Unspecified. Treating Unspecified as UTC matches the convention that
    /// all datetime values stored in the database represent UTC instants.
    /// </summary>
    internal static DateTime NormalizeDateTime(DateTime dt) => ConvertToUtc(dt);

    internal static DateTime ConvertToUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static DateTimeOffset CreateFlexibleOffset(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        if (dt.Kind == DateTimeKind.Local)
        {
            return new DateTimeOffset(dt);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero);
    }
}
