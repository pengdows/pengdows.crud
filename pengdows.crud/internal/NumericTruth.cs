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

    private static InvalidCastException NotABoolean(IFormattable value) =>
        new($"{value.ToString(null, CultureInfo.InvariantCulture)} is not a number; it reads as neither true nor false.");
}
