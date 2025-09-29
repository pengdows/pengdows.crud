using System;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

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

    protected override bool TryConvertFromProvider(object value, SupportedDatabase provider, out PostgreSqlInterval result)
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
                        var months = monthsProp != null ? Convert.ToInt32(monthsProp.GetValue(value), CultureInfo.InvariantCulture) : 0;
                        var days = daysProp != null ? Convert.ToInt32(daysProp.GetValue(value), CultureInfo.InvariantCulture) : 0;
                        var ticks = ticksProp != null ? Convert.ToInt64(ticksProp.GetValue(value), CultureInfo.InvariantCulture) : 0L;
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

        var totalSeconds = (hours * 3600d) + (minutes * 60d) + seconds;
        return (long)(totalSeconds * 1_000_000d);
    }
}
