// =============================================================================
// FILE: PostgreSqlIntervalConverter.cs
// PURPOSE: Converter for PostgreSQL INTERVAL type (complex time duration).
//
// AI SUMMARY:
// - Converts between database interval values and PostgreSqlInterval value objects.
// - Supports years, months, days, hours, minutes, seconds, and microseconds.
// - Provider-specific:
//   * PostgreSQL/CockroachDB: INTERVAL type with ISO 8601 output
//   * Others: Raw value (application-level storage)
// - ConvertToProvider(): Returns ISO 8601 format (P3Y6M4DT12H30M5S) for PostgreSQL.
// - TryConvertFromProvider(): Handles PostgreSqlInterval, TimeSpan, string, NpgsqlTimeSpan.
// - Parse(): Handles ISO 8601 duration and PostgreSQL text format.
// - Components: Months (includes years), Days, Microseconds (sub-day time).
// - Thread-safe and immutable value objects.
// =============================================================================

using System.Globalization;
using pengdows.crud.enums;
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
/// <item><description>string → PostgreSqlInterval (parses ISO 8601 duration or PostgreSQL interval format)</description></item>
/// <item><description>NpgsqlTimeSpan → PostgreSqlInterval (converts Npgsql provider-specific type via reflection)</description></item>
/// </list>
/// <para><strong>Format:</strong> Supports ISO 8601 duration format (P3Y6M4DT12H30M5S) and PostgreSQL text format.
/// Output format is ISO 8601 for PostgreSQL/CockroachDB providers.</para>
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
    protected override object? ConvertToProvider(PostgreSqlInterval value, SupportedDatabase provider)
    {
        if (provider != SupportedDatabase.PostgreSql && provider != SupportedDatabase.CockroachDb)
        {
            return value;
        }

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
        var builder = new System.Text.StringBuilder();
        builder.Append('P');
        if (value.Months != 0)
        {
            builder.Append(value.Months.ToString(CultureInfo.InvariantCulture)).Append('M');
        }

        if (value.Days != 0)
        {
            builder.Append(value.Days.ToString(CultureInfo.InvariantCulture)).Append('D');
        }

        var hasTime = value.Microseconds != 0;
        if (hasTime)
        {
            var time = TimeSpan.FromTicks(value.Microseconds * 10);
            builder.Append('T');
            if (time.Hours != 0)
            {
                builder.Append(time.Hours.ToString(CultureInfo.InvariantCulture)).Append('H');
            }

            if (time.Minutes != 0)
            {
                builder.Append(time.Minutes.ToString(CultureInfo.InvariantCulture)).Append('M');
            }

            if (time.Seconds != 0 || time.Milliseconds != 0)
            {
                var seconds = time.Seconds + time.Milliseconds / 1000.0;
                builder.Append(seconds.ToString(CultureInfo.InvariantCulture)).Append('S');
            }
        }

        if (builder.Length == 1)
        {
            builder.Append("0D");
        }

        return builder.ToString();
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

                if (c == 'M' && number.Length > 0)
                {
                    months = int.Parse(number, CultureInfo.InvariantCulture);
                }
                else if (c == 'D' && number.Length > 0)
                {
                    days = int.Parse(number, CultureInfo.InvariantCulture);
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