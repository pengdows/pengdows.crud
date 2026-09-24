// =============================================================================
// FILE: PostgreSqlIntervalConverter.cs
// PURPOSE: Converter for PostgreSQL INTERVAL type (complex time duration).
//
// AI SUMMARY:
// - Converts between database interval values and PostgreSqlInterval value objects.
// - Supports years, months, days, hours, minutes, seconds, and microseconds.
// - Provider-specific:
//   * PostgreSQL/CockroachDB/YugabyteDB: INTERVAL, written as Npgsql's NpgsqlInterval
//   * Others: Raw value (application-level storage)
// - ConvertToProvider(): For the PostgreSQL family returns NpgsqlTypes.NpgsqlInterval (months,
//   days, microseconds — the only Npgsql write type that keeps months; resolved by name). Falls
//   back to ISO 8601-style text only when Npgsql isn't loaded.
// - TryConvertFromProvider(): Handles PostgreSqlInterval, NpgsqlInterval, TimeSpan, string,
//   NpgsqlTimeSpan. Hydration reads interval columns as NpgsqlInterval (IntervalFieldReader), so
//   months and the stored days/time split round-trip.
// - Parse(): Handles ISO 8601-style durations (Y/M/W/D date part — years fold into months, weeks
//   into days — and H/M/S time part); PostgreSQL's verbose text format ("1 year 2 mons") is not supported.
// - Components: Months (includes years), Days, Microseconds (sub-day time).
// - Thread-safe and immutable value objects.
// =============================================================================

using System.Globalization;
using System.Reflection;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database interval values and <see cref="PostgreSqlInterval"/> value objects.
/// Supports PostgreSQL's native INTERVAL type with years, months, days, and sub-day time components.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>PostgreSQL:</strong> Maps to INTERVAL type. Supports years, months, days, hours, minutes, seconds, and microseconds.</description></item>
/// <item><description><strong>CockroachDB:</strong> Maps to INTERVAL type (PostgreSQL compatible).</description></item>
/// <item><description><strong>Other databases:</strong> No native PostgreSQL-style interval type. Fallback to application-level storage.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>PostgreSqlInterval → PostgreSqlInterval (pass-through)</description></item>
/// <item><description>TimeSpan → PostgreSqlInterval (converts via PostgreSqlInterval.FromTimeSpan)</description></item>
/// <item><description>string → PostgreSqlInterval (parses ISO 8601-style durations with Y/M/W/D and H/M/S components)</description></item>
/// <item><description>NpgsqlTimeSpan → PostgreSqlInterval (converts Npgsql provider-specific type via reflection)</description></item>
/// </list>
/// <para><strong>Format:</strong> Reads ISO 8601-style durations such as "P3Y6M4DT12H30M5S" (years fold into
/// months, weeks into days; PostgreSQL's verbose text format is not parsed). For PostgreSQL/CockroachDB/YugabyteDB
/// the value is written as Npgsql's <c>NpgsqlInterval</c>, which keeps months; ISO 8601 text is only a fallback
/// when Npgsql isn't loaded.</para>
/// <para><strong>Components:</strong> PostgreSqlInterval has three fields: Months (includes years), Days, and Microseconds (sub-day time).
/// This matches PostgreSQL's internal representation.</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. PostgreSqlInterval value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with interval
/// [Table("events")]
/// public class Event
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("duration", DbType.Object)]
///     public PostgreSqlInterval Duration { get; set; }
/// }
///
/// // Create with interval (3 years, 6 months, 4 days, 12 hours, 30 minutes)
/// var evt = new Event
/// {
///     Duration = new PostgreSqlInterval(months: 42, days: 4, microseconds: 45000000000) // 12.5 hours in microseconds
/// };
/// await helper.CreateAsync(evt);
///
/// // Convert from TimeSpan
/// var evt2 = new Event
/// {
///     Duration = PostgreSqlInterval.FromTimeSpan(TimeSpan.FromHours(24))
/// };
/// await helper.CreateAsync(evt2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(evt.Id);
/// Console.WriteLine($"Months: {retrieved.Duration.Months}");  // 42
/// Console.WriteLine($"Days: {retrieved.Duration.Days}");      // 4
/// </code>
/// </example>
internal sealed class PostgreSqlIntervalConverter : AdvancedTypeConverter<PostgreSqlInterval>
{
    // NpgsqlTypes.NpgsqlInterval(int months, int days, long time) — resolved by name so pengdows.crud
    // takes no dependency on Npgsql. It is the only Npgsql write type that keeps months; a string
    // is rejected for NpgsqlDbType.Interval, and TimeSpan cannot represent months.
    private static readonly Lazy<ConstructorInfo?> NpgsqlIntervalCtor = new(() =>
        Type.GetType("NpgsqlTypes.NpgsqlInterval, Npgsql", throwOnError: false)
            ?.GetConstructor(new[] { typeof(int), typeof(int), typeof(long) }));

    protected override object? ConvertToProvider(PostgreSqlInterval value, SupportedDatabase provider)
    {
        if (provider is not (SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb
            or SupportedDatabase.YugabyteDb))
        {
            return value;
        }

        var ctor = NpgsqlIntervalCtor.Value;
        if (ctor != null)
        {
            return ctor.Invoke(new object[] { value.Months, value.Days, value.Microseconds });
        }

        // Npgsql not loaded (no Npgsql connection is possible): fall back to ISO 8601 text.
        return FormatIso8601(value);
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out PostgreSqlInterval result)
    {
        try
        {
            switch (value)
            {
                case PostgreSqlInterval interval:
                    result = interval;
                    return true;
                case TimeSpan time:
                    result = PostgreSqlInterval.FromTimeSpan(time);
                    return true;
                case string text:
                    result = Parse(text);
                    return true;
                default:
                    {
                        var type = value.GetType();
                        if (type.FullName == "NpgsqlTypes.NpgsqlInterval")
                        {
                            // Npgsql's full-fidelity interval (months, days, microseconds), read by
                            // IntervalFieldReader so months and the stored days/time split survive.
                            result = new PostgreSqlInterval(
                                Convert.ToInt32(type.GetProperty("Months")!.GetValue(value), CultureInfo.InvariantCulture),
                                Convert.ToInt32(type.GetProperty("Days")!.GetValue(value), CultureInfo.InvariantCulture),
                                Convert.ToInt64(type.GetProperty("Time")!.GetValue(value), CultureInfo.InvariantCulture));
                            return true;
                        }

                        if (type.FullName?.Contains("NpgsqlTimeSpan", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            var monthsProp = type.GetProperty("Months");
                            var daysProp = type.GetProperty("Days");
                            var ticksProp = type.GetProperty("Ticks");
                            var months = monthsProp != null
                                ? Convert.ToInt32(monthsProp.GetValue(value), CultureInfo.InvariantCulture)
                                : 0;
                            var days = daysProp != null
                                ? Convert.ToInt32(daysProp.GetValue(value), CultureInfo.InvariantCulture)
                                : 0;
                            var ticks = ticksProp != null
                                ? Convert.ToInt64(ticksProp.GetValue(value), CultureInfo.InvariantCulture)
                                : 0L;
                            var microseconds = ticks / 10;
                            result = new PostgreSqlInterval(months, days, microseconds);
                            return true;
                        }

                        result = default!;
                        return false;
                    }
            }
        }
        catch
        {
            result = default!;
            return false;
        }
    }

    private static string FormatIso8601(PostgreSqlInterval value)
    {
        var builder = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        builder.Append('P');
        if (value.Months != 0)
        {
            builder.Append(value.Months);
            builder.Append('M');
        }

        if (value.Days != 0)
        {
            builder.Append(value.Days);
            builder.Append('D');
        }

        if (value.Microseconds != 0)
        {
            // Work from total microseconds: TimeSpan's Hours/Seconds components would drop whole
            // days held in the time part and anything below a millisecond.
            const long MicrosPerHour = 3_600_000_000L;
            const long MicrosPerMinute = 60_000_000L;
            var hours = value.Microseconds / MicrosPerHour;
            var remainder = value.Microseconds % MicrosPerHour;
            var minutes = remainder / MicrosPerMinute;
            remainder %= MicrosPerMinute;

            builder.Append('T');
            if (hours != 0)
            {
                builder.Append(hours);
                builder.Append('H');
            }

            if (minutes != 0)
            {
                builder.Append(minutes);
                builder.Append('M');
            }

            if (remainder != 0)
            {
                builder.Append((remainder / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture));
                builder.Append('S');
            }
        }

        if (builder.Length == 1)
        {
            builder.Append("0D");
        }

        var result = builder.ToString();
        builder.Dispose();
        return result;
    }

    private static PostgreSqlInterval Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PostgreSqlInterval(0, 0, 0);
        }

        var months = 0;
        var days = 0;
        long microseconds = 0;

        var remaining = text.Trim();
        if (remaining.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            remaining = remaining.Substring(1);
        }

        var timeIndex = remaining.IndexOf('T');
        string? timePart = null;
        if (timeIndex >= 0)
        {
            timePart = remaining.Substring(timeIndex + 1);
            remaining = remaining.Substring(0, timeIndex);
        }

        if (!string.IsNullOrEmpty(remaining))
        {
            var number = string.Empty;
            foreach (var c in remaining)
            {
                if (char.IsDigit(c) || c == '-' || c == '+')
                {
                    number += c;
                    continue;
                }

                if (number.Length > 0)
                {
                    var n = int.Parse(number, CultureInfo.InvariantCulture);
                    switch (c)
                    {
                        case 'Y':
                            months += n * 12;
                            break;
                        case 'M':
                            months += n;
                            break;
                        case 'W':
                            days += n * 7;
                            break;
                        case 'D':
                            days += n;
                            break;
                    }
                }

                number = string.Empty;
            }
        }

        if (!string.IsNullOrEmpty(timePart))
        {
            microseconds = ParseTimeComponent(timePart);
        }

        return new PostgreSqlInterval(months, days, microseconds);
    }

    private static long ParseTimeComponent(string timePart)
    {
        var buffer = string.Empty;
        var hours = 0;
        var minutes = 0;
        var seconds = 0.0;

        foreach (var c in timePart)
        {
            if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
            {
                buffer += c;
                continue;
            }

            if (buffer.Length == 0)
            {
                continue;
            }

            switch (c)
            {
                case 'H':
                    hours = int.Parse(buffer, CultureInfo.InvariantCulture);
                    break;
                case 'M':
                    minutes = int.Parse(buffer, CultureInfo.InvariantCulture);
                    break;
                case 'S':
                    seconds = double.Parse(buffer, CultureInfo.InvariantCulture);
                    break;
            }

            buffer = string.Empty;
        }

        var totalSeconds = hours * 3600d + minutes * 60d + seconds;
        return (long)(totalSeconds * 1_000_000d);
    }
}