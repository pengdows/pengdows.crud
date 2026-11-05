// =============================================================================
// FILE: BitStringFormat.cs
// PURPOSE: A BitArray as a bit string ("10110", first bit first) and back.
//
// AI SUMMARY:
// - DuckDB BIT reads back as its bit string and is written from one; a BitArray parameter is bound
//   as its ToString() (confirmed live, DuckDB.NET 1.5.6). DuckDbDialect writes with Format; the
//   BitArray coercion reads with TryParse.
// - TryParse accepts only '0' and '1', so other text fails loudly rather than becoming bits.
// =============================================================================

using System.Collections;

namespace pengdows.crud.@internal;

internal static class BitStringFormat
{
    public static string Format(BitArray bits)
    {
        return string.Create(bits.Length, bits, static (chars, source) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = source[i] ? '1' : '0';
            }
        });
    }

    /// <summary>
    /// A fixed-width BIT(<paramref name="width"/>) column's integer as its bits, first bit first (the value 5 at width 3 is
    /// "101"). A value that does not fit the width is refused, never truncated.
    /// </summary>
    public static BitArray FromBitField(ulong value, int width)
    {
        if (width is < 1 or > 64)
        {
            throw new System.ArgumentOutOfRangeException(nameof(width), width, "A BIT column is 1 to 64 bits wide.");
        }

        if (width < 64 && value >> width != 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(value), value, $"{value} does not fit in BIT({width}).");
        }

        var bits = new BitArray(width);
        for (var i = 0; i < width; i++)
        {
            bits[i] = ((value >> (width - 1 - i)) & 1) == 1;
        }

        return bits;
    }

    public static bool TryParse(string text, out BitArray bits)
    {
        bits = new BitArray(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '1':
                    bits[i] = true;
                    break;
                case '0':
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
