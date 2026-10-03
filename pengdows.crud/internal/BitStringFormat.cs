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
