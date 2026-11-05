// =============================================================================
// FILE: BitFieldReader.cs
// PURPOSE: Reads a fixed-width BIT(n) column's value as a BitArray of its declared width.
//
// AI SUMMARY:
// - MySQL-family drivers return a BIT(n) column as an integer (MySqlConnector: ulong) or as its bytes, most significant
//   first (MySql.Data). Neither carries the width, so the declared NativeType "BIT(n)" supplies it.
// - WidthOf parses the n from a declared type name; null for any other type.
// - Read returns the bits of the integer or the bytes, or null for NULL.
// =============================================================================

using System;
using System.Collections;
using System.Globalization;

namespace pengdows.crud.@internal;

internal static class BitFieldReader
{
    /// <summary>The n of a declared BIT(n) type, or null when the type is not a fixed-width bit field.</summary>
    public static int? WidthOf(string? nativeType)
    {
        if (nativeType is null || !nativeType.StartsWith("BIT(", StringComparison.OrdinalIgnoreCase) ||
            !nativeType.EndsWith(')'))
        {
            return null;
        }

        var inner = nativeType.Substring(4, nativeType.Length - 5);
        return int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out var width) ? width : null;
    }

    /// <summary>The column's bits at <paramref name="width"/>, or null for NULL.</summary>
    public static BitArray? Read(object? raw, int width) => raw switch
    {
        null or DBNull => null,
        BitArray bits => bits,
        byte[] bytes => BitStringFormat.FromBitField(BigEndian(bytes), width),
        _ => BitStringFormat.FromBitField(Convert.ToUInt64(raw, CultureInfo.InvariantCulture), width),
    };

    private static ulong BigEndian(byte[] bytes)
    {
        ulong value = 0;
        foreach (var b in bytes)
        {
            value = (value << 8) | b;
        }

        return value;
    }

    /// <summary>The method the mapper calls for a BIT(n) column.</summary>
    public static readonly System.Reflection.MethodInfo ReadMethod =
        typeof(BitFieldReader).GetMethod(nameof(Read))!;
}
