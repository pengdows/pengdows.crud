using System;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

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

    protected override bool TryConvertFromProvider(object value, SupportedDatabase provider, out IntervalDaySecond result)
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
        string? datePart = trimmed;
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
