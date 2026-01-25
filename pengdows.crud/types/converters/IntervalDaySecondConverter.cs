using System;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database interval day-to-second values and <see cref="IntervalDaySecond"/> value objects.
/// Represents Oracle INTERVAL DAY TO SECOND type with days and sub-day time components.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Oracle:</strong> Maps to INTERVAL DAY TO SECOND type. Format: P5DT12H30M45.5S (ISO 8601).</description></item>
/// <item><description><strong>PostgreSQL/CockroachDB:</strong> Can be stored as INTERVAL and formatted as ISO 8601.</description></item>
/// <item><description><strong>Other databases:</strong> Stores as TimeSpan equivalent. No native interval day-second type.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>IntervalDaySecond → IntervalDaySecond (pass-through)</description></item>
/// <item><description>TimeSpan → IntervalDaySecond (converts via IntervalDaySecond.FromTimeSpan)</description></item>
/// <item><description>string → IntervalDaySecond (parses ISO 8601 duration format like P5DT12H30M45.5S)</description></item>
/// </list>
/// <para><strong>Format:</strong> Uses ISO 8601 duration format for Oracle and PostgreSQL providers.
/// Example: P5DT12H30M45.5S represents 5 days, 12 hours, 30 minutes, 45.5 seconds.</para>
/// <para><strong>Components:</strong> IntervalDaySecond has Days (integer) and Time (TimeSpan for hours/minutes/seconds).
/// Maximum precision depends on database (Oracle supports up to 9 digits of fractional seconds).</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. IntervalDaySecond value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with day-second interval
/// [Table("tasks")]
/// public class Task
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("estimated_duration", DbType.Object)]
///     public IntervalDaySecond EstimatedDuration { get; set; }
/// }
///
/// // Create with interval (5 days, 12 hours, 30 minutes)
/// var task = new Task
/// {
///     EstimatedDuration = new IntervalDaySecond(days: 5, time: new TimeSpan(12, 30, 0))
/// };
/// await helper.CreateAsync(task);
///
/// // Convert from TimeSpan
/// var task2 = new Task
/// {
///     EstimatedDuration = IntervalDaySecond.FromTimeSpan(TimeSpan.FromDays(2.5))
/// };
/// await helper.CreateAsync(task2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(task.Id);
/// Console.WriteLine($"Days: {retrieved.EstimatedDuration.Days}");            // 5
/// Console.WriteLine($"Time: {retrieved.EstimatedDuration.Time}");            // 12:30:00
/// Console.WriteLine($"Total: {retrieved.EstimatedDuration.TotalTime}");      // 5.12:30:00
/// </code>
/// </example>
internal sealed class IntervalDaySecondConverter : AdvancedTypeConverter<IntervalDaySecond>
{
    protected override object? ConvertToProvider(IntervalDaySecond value, SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Oracle => FormatIso(value),
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => FormatIso(value),
            _ => value.TotalTime
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out IntervalDaySecond result)
    {
        try
        {
            switch (value)
            {
                case IntervalDaySecond interval:
                    result = interval;
                    return true;
                case TimeSpan span:
                    result = IntervalDaySecond.FromTimeSpan(span);
                    return true;
                case string text:
                    result = Parse(text);
                    return true;
                default:
                    result = default!;
                    return false;
            }
        }
        catch
        {
            result = default!;
            return false;
        }
    }

    private static string FormatIso(IntervalDaySecond value)
    {
        var time = value.Time;
        return string.Concat(
            "P",
            value.Days.ToString(CultureInfo.InvariantCulture),
            "DT",
            time.Hours.ToString(CultureInfo.InvariantCulture),
            "H",
            time.Minutes.ToString(CultureInfo.InvariantCulture),
            "M",
            (time.Seconds + time.Milliseconds / 1000.0).ToString(CultureInfo.InvariantCulture),
            "S");
    }

    private static IntervalDaySecond Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntervalDaySecond(0, TimeSpan.Zero);
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }

        var timeIndex = trimmed.IndexOf('T');
        var datePart = trimmed;
        string? timePart = null;
        if (timeIndex >= 0)
        {
            datePart = trimmed.Substring(0, timeIndex);
            timePart = trimmed.Substring(timeIndex + 1);
        }

        var days = 0;
        if (!string.IsNullOrEmpty(datePart))
        {
            var buffer = string.Empty;
            foreach (var c in datePart)
            {
                if (char.IsDigit(c) || c == '-' || c == '+')
                {
                    buffer += c;
                    continue;
                }

                if (buffer.Length == 0)
                {
                    continue;
                }

                if (c == 'D')
                {
                    days = int.Parse(buffer, CultureInfo.InvariantCulture);
                }

                buffer = string.Empty;
            }
        }

        var hours = 0;
        var minutes = 0;
        var seconds = 0.0;

        if (!string.IsNullOrEmpty(timePart))
        {
            var buffer = string.Empty;
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
        }

        var timeSpan = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return new IntervalDaySecond(days, timeSpan);
    }
}