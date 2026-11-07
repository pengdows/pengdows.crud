// =============================================================================
// FILE: WideIntegerCoercion.cs
// PURPOSE: Converts the 128-bit and arbitrary-size integers to and from the integer types, decimal, double and text.
//
// AI SUMMARY:
// - IsWideInteger: BigInteger, Int128 and UInt128.
// - TryCoerceWideInteger: the exact value of a wide integer, or of a whole number or its text, in the target type. A value
//   that is not a whole number declines, and the general conversion takes it.
// =============================================================================

using System;
using System.Globalization;
using System.Numerics;

namespace pengdows.crud.types.coercion;

internal static class WideIntegerCoercion
{
    internal static bool IsWideInteger(Type type) =>
        type == typeof(BigInteger) || type == typeof(Int128) || type == typeof(UInt128);

    internal static bool TryCoerceWideInteger(object value, Type target, out object? result)
    {
        result = null;
        if (!IsWideInteger(value.GetType()) && !IsWideInteger(target))
        {
            return false;
        }

        BigInteger source;
        switch (value)
        {
            case BigInteger big: source = big; break;
            case Int128 i128: source = i128; break;
            case UInt128 u128: source = u128; break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                source = new BigInteger(Convert.ToDecimal(value, CultureInfo.InvariantCulture)); break;
            case decimal d when decimal.Truncate(d) == d: source = new BigInteger(d); break;
            case string text: source = BigInteger.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); break;
            default: return false;
        }

        result = Type.GetTypeCode(target) switch
        {
            TypeCode.SByte => (object)(sbyte)source,
            TypeCode.Byte => (byte)source,
            TypeCode.Int16 => (short)source,
            TypeCode.UInt16 => (ushort)source,
            TypeCode.Int32 => (int)source,
            TypeCode.UInt32 => (uint)source,
            TypeCode.Int64 => (long)source,
            TypeCode.UInt64 => (ulong)source,
            TypeCode.Decimal => (decimal)source,
            // (double)BigInteger truncates; parsing the exact digits rounds to nearest, like
            // (double)decimal does.
            TypeCode.Double => double.Parse(source.ToString(CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture),
            TypeCode.String => source.ToString(CultureInfo.InvariantCulture),
            _ when target == typeof(BigInteger) => source,
            _ when target == typeof(Int128) => (Int128)source,
            _ when target == typeof(UInt128) => (UInt128)source,
            _ => null
        };
        return result != null;
    }
}
