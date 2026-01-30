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
// - Uses a pre-computed Pow10 lookup table for fast multiplication.
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
public static class DecimalHelpers
{
    /// <summary>
    /// Pre-computed powers of 10 for fast decimal multiplication.
    /// </summary>
    private static readonly decimal[] Pow10 =
    {
        1m, 10m, 100m, 1000m, 10000m, 100000m, 1000000m, 10000000m,
        100000000m, 1000000000m, 10000000000m, 100000000000m,
        1000000000000m, 10000000000000m, 100000000000000m,
        1000000000000000m, 10000000000000000m, 100000000000000000m,
        1000000000000000000m, 10000000000000000000m, 100000000000000000000m,
        1000000000000000000000m, 10000000000000000000000m,
        100000000000000000000000m, 1000000000000000000000000m,
        10000000000000000000000000m, 100000000000000000000000000m,
        1000000000000000000000000000m, 10000000000000000000000000000m
    };

    /// <summary>
    /// Returns (Precision, Scale) per SQL DECIMAL semantics:
    /// - Precision = digits left of decimal + Scale
    /// - Scale = digits right of decimal, with trailing fractional zeros trimmed.
    /// - 0m => (0,0)
    /// </summary>
    public static (int Precision, int Scale) Infer(decimal value)
    {
        if (value == 0m)
        {
            return (0, 0);
        }

        var abs = Math.Abs(value);

        // encoded base-10 scale from decimal
        var bits = decimal.GetBits(abs);
        var scale = (bits[3] >> 16) & 0x7F; // 0..28

        // Build exact integer mantissa so we can trim trailing fractional zeros
        var mantissa = scale == 0 ? abs : abs * Pow10[scale];

        // Trim trailing zeros from the fractional part (i.e., remove factors of 10)
        while (scale > 0 && mantissa % 10m == 0m)
        {
            mantissa /= 10m;
            scale--;
        }

        // Count integer digits (digits to the left of the decimal point)
        var integerDigits = 0;
        var intPart = decimal.Truncate(abs);
        while (intPart >= 1m)
        {
            intPart /= 10m;
            integerDigits++;
        }

        var precision = integerDigits + scale;
        return (precision, scale);
    }
}