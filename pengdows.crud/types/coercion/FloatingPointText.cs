using System.Globalization;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Parses the text a provider returns for a floating-point value. .NET's parsers accept "Infinity" and "NaN" but reject
/// the spellings databases use: Snowflake writes the infinities as "inf" and "-inf". Every floating-point text read goes
/// through here, so one spelling is accepted everywhere.
/// </summary>
internal static class FloatingPointText
{
    internal static double ParseDoubleOrThrow(string text)
    {
        if (TryParseSpecial(text, out var special))
        {
            return special;
        }

        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    internal static float ParseSingleOrThrow(string text)
    {
        if (TryParseSpecial(text, out var special))
        {
            return (float)special;
        }

        return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // The spellings .NET's parsers don't take (case-insensitive, optional sign on the infinities).
    private static bool TryParseSpecial(string text, out double value)
    {
        var trimmed = text.Trim();
        switch (trimmed.ToLowerInvariant())
        {
            case "inf" or "+inf" or "infinity" or "+infinity":
                value = double.PositiveInfinity;
                return true;
            case "-inf" or "-infinity":
                value = double.NegativeInfinity;
                return true;
            case "nan":
                value = double.NaN;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
