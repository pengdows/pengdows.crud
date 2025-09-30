using System;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

internal sealed class IntervalYearMonthConverter : AdvancedTypeConverter<IntervalYearMonth>
{
    protected override object? ConvertToProvider(IntervalYearMonth value, SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Oracle => FormatIso(value),
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => FormatIso(value),
            _ => value
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out IntervalYearMonth result)
    {
        if (value is IntervalYearMonth interval)
        {
            result = interval;
            return true;
        }

        if (value is string text)
        {
            try
            {
                result = Parse(text);
                return true;
            }
            catch
            {
                result = default!;
                return false;
            }
        }

        result = default!;
        return false;
    }

    private static string FormatIso(IntervalYearMonth value)
    {
        return string.Concat(
            "P",
            value.Years.ToString(CultureInfo.InvariantCulture),
            "Y",
            value.Months.ToString(CultureInfo.InvariantCulture),
            "M");
    }

    private static IntervalYearMonth Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntervalYearMonth(0, 0);
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }

        var years = 0;
        var months = 0;
        var buffer = string.Empty;

        foreach (var c in trimmed)
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

            switch (c)
            {
                case 'Y':
                    years = int.Parse(buffer, CultureInfo.InvariantCulture);
                    break;
                case 'M':
                    months = int.Parse(buffer, CultureInfo.InvariantCulture);
                    break;
            }

            buffer = string.Empty;
        }

        return new IntervalYearMonth(years, months);
    }
}
