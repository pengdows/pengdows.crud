// =============================================================================
// FILE: DecimalHelpers.cs
// PURPOSE: Utilities for analyzing decimal values to infer SQL DECIMAL
//          precision and scale for dynamic schema/parameter configuration.
//
// AI SUMMARY:
// - Provides the Infer() method which analyzes a decimal value and returns
//   its (Precision, Scale) in SQL DECIMAL semantics.
// - Precision = total significant digits (left + right of decimal point).
// - Scale = digits to the right of the decimal point (after trimming zeros).
// - Works on the 96-bit integer mantissa (stack-allocated GetBits, a powers-of-ten table):
//   no allocation and no decimal division, since it runs on every decimal parameter.
// - Handles trailing fractional zeros correctly (1.2300 => scale 2, not 4).
// - Returns (0, 0) for zero values.
// - Use case: When you need to dynamically determine the appropriate
//   DECIMAL(p,s) type for a value, or validate that a value fits within
//   a column's declared precision/scale constraints.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// Provides utility methods for analyzing decimal values.
/// </summary>
/// <remarks>
/// This class is used internally to determine the precision and scale of decimal
/// values for SQL parameter configuration and validation.
/// </remarks>
internal static class DecimalHelpers
{
    // 10^0 .. 10^29: a decimal's 96-bit mantissa has at most 29 digits.
    private static readonly UInt128[] PowersOfTen = BuildPowersOfTen();

    private static UInt128[] BuildPowersOfTen()
    {
        var powers = new UInt128[30];
        powers[0] = 1;
        for (var i = 1; i < powers.Length; i++)
        {
            powers[i] = powers[i - 1] * 10;
        }

        return powers;
    }

    /// <summary>
    /// Returns (Precision, Scale) per SQL DECIMAL semantics:
    /// - Precision = digits left of decimal + Scale
    /// - Scale = digits right of decimal, with trailing fractional zeros trimmed.
    /// - 0m => (0,0)
    /// Runs on every decimal parameter, so it works on the integer mantissa: no allocation and no
    /// decimal division (which cost about 400 ns for a 17-digit value).
    /// </summary>
    public static (int Precision, int Scale) Infer(decimal value)
    {
        if (value == 0m)
        {
            return (0, 0);
        }

        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var mantissa = ((UInt128)(uint)bits[2] << 64) | ((UInt128)(uint)bits[1] << 32) | (uint)bits[0];
        int scale = value.Scale;

        // Trim trailing zeros from the fractional part (i.e., remove factors of 10)
        while (scale > 0 && (mantissa % 10) == 0)
        {
            mantissa /= 10;
            scale--;
        }

        var integerDigits = Math.Max(0, DigitCount(mantissa) - scale);
        return (integerDigits + scale, scale);
    }

    private static int DigitCount(UInt128 mantissa)
    {
        var digits = 1;
        while (digits < PowersOfTen.Length && mantissa >= PowersOfTen[digits])
        {
            digits++;
        }

        return digits;
    }
}