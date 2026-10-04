using System.Globalization;

namespace pengdows.crud.@internal;

/// <summary>
/// A number read into a bool: non-zero is true (DRY-003). NaN is neither and fails; the gateway, the
/// registry and DataReaderMapper used to disagree (false, false, true), and a value just above zero
/// read as false through a float-epsilon test.
/// </summary>
internal static class NumericTruth
{
    public static bool FromDouble(double value) =>
        double.IsNaN(value) ? throw NotABoolean(value) : value != 0;

    public static bool FromFloat(float value) =>
        float.IsNaN(value) ? throw NotABoolean(value) : value != 0;

    /// <summary>Throws when <paramref name="value"/> is a NaN read into a bool (Coerce's fallback would read it as true).</summary>
    public static void Check(object value, Type target)
    {
        if (target != typeof(bool))
        {
            return;
        }

        switch (value)
        {
            case double d:
                FromDouble(d);
                break;
            case float f:
                FromFloat(f);
                break;
        }
    }

    private static InvalidCastException NotABoolean(IFormattable value) =>
        new($"{value.ToString(null, CultureInfo.InvariantCulture)} is not a number; it reads as neither true nor false.");
}
