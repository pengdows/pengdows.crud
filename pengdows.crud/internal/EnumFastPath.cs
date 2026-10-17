// =============================================================================
// FILE: EnumFastPath.cs
// PURPOSE: Reads an integer column into an enum without boxing, for values that are certainly valid.
//
// AI SUMMARY:
// - A value that is exactly a defined member of the enum is converted with no allocation and no dictionary lookup
//   (a bitmask for members 0..63, a binary search beyond).
// - Everything else (an undefined number, a [Flags] combination, a value the underlying type cannot hold, a
//   fractional number) is NOT decided here: the fast path returns false and the caller uses the full conversion
//   (TypeCoercionHelper's CoerceEnum, with its failure modes), so no rule or failure mode is duplicated.
// - Membership is by the exact numeric value, so a long whose low bits happen to equal a member is rejected.
// - Used by DataReaderMapper (integer column into an enum property) and EnumMappingCache.ValidateEnumValue.
// =============================================================================

using System.Reflection;
using System.Runtime.CompilerServices;

namespace pengdows.crud.@internal;

internal static class EnumFastPath
{
    private static readonly bool LittleEndian = BitConverter.IsLittleEndian;

    // One static holder per enum type, built once.
    private static class Info<TEnum> where TEnum : struct, Enum
    {
        public static readonly int Size = Unsafe.SizeOf<TEnum>();

        public static readonly bool Signed = IsSigned(Enum.GetUnderlyingType(typeof(TEnum)));

        // Bit n set when the enum defines the value n, for 0..63.
        public static readonly ulong SmallMask;

        // The defined values outside 0..63, sorted (a ulong member above long.MaxValue cannot equal a long, so is left out).
        public static readonly long[] Sorted;

        static Info()
        {
            var large = new List<long>();
            foreach (var member in Enum.GetValues<TEnum>())
            {
                if (!TryWiden(member, out var value))
                {
                    continue;
                }

                if (value is >= 0 and < 64)
                {
                    SmallMask |= 1UL << (int)value;
                }
                else
                {
                    large.Add(value);
                }
            }

            large.Sort();
            Sorted = large.ToArray();
        }

        private static bool IsSigned(Type underlying) =>
            underlying == typeof(sbyte) || underlying == typeof(short) || underlying == typeof(int) ||
            underlying == typeof(long);

        // The member's value as a long, by its underlying type; false for a ulong member a long cannot hold.
        public static bool TryWiden(TEnum member, out long value)
        {
            switch (Size)
            {
                case 1:
                    value = Signed ? Unsafe.As<TEnum, sbyte>(ref member) : Unsafe.As<TEnum, byte>(ref member);
                    return true;
                case 2:
                    value = Signed ? Unsafe.As<TEnum, short>(ref member) : Unsafe.As<TEnum, ushort>(ref member);
                    return true;
                case 4:
                    value = Signed ? Unsafe.As<TEnum, int>(ref member) : Unsafe.As<TEnum, uint>(ref member);
                    return true;
                default:
                    var wide = Unsafe.As<TEnum, long>(ref member);
                    value = wide;
                    return Signed || wide >= 0;
            }
        }
    }

    /// <summary>True, with the member, when <paramref name="value"/> is exactly a defined member of the enum.</summary>
    public static bool TryFromInteger<TEnum>(long value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        if (!LittleEndian || !Contains<TEnum>(value))
        {
            return false;
        }

        // The low bytes of a little-endian long are the value as the enum's underlying type (it was found defined).
        result = Unsafe.As<long, TEnum>(ref value);
        return true;
    }

    /// <summary>True when <paramref name="value"/> is a defined member (a flags combination is not one).</summary>
    public static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Info<TEnum>.TryWiden(value, out var widened) && Contains<TEnum>(widened);

    private static bool Contains<TEnum>(long value) where TEnum : struct, Enum =>
        (ulong)value < 64
            ? ((Info<TEnum>.SmallMask >> (int)value) & 1UL) != 0
            : Array.BinarySearch(Info<TEnum>.Sorted, value) >= 0;

    /// <summary>An integer column into a non-nullable enum: the member when it is one, else the full conversion.</summary>
    public static TEnum Resolve<TEnum>(long value, Func<object?, object?> fullConversion) where TEnum : struct, Enum =>
        TryFromInteger<TEnum>(value, out var member) ? member : (TEnum)fullConversion(value)!;

    /// <summary>An integer column into a nullable enum: the member when it is one, else the full conversion.</summary>
    public static TEnum? ResolveNullable<TEnum>(long value, Func<object?, object?> fullConversion)
        where TEnum : struct, Enum =>
        TryFromInteger<TEnum>(value, out var member) ? member : (TEnum?)fullConversion(value);

    internal static readonly MethodInfo ResolveMethod =
        typeof(EnumFastPath).GetMethod(nameof(Resolve), BindingFlags.Public | BindingFlags.Static)!;

    internal static readonly MethodInfo ResolveNullableMethod =
        typeof(EnumFastPath).GetMethod(nameof(ResolveNullable), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>The integer column types the fast path reads (ulong is left to the full conversion).</summary>
    internal static bool Reads(Type fieldType) =>
        fieldType == typeof(int) || fieldType == typeof(long) || fieldType == typeof(short) ||
        fieldType == typeof(byte) || fieldType == typeof(sbyte) || fieldType == typeof(ushort) ||
        fieldType == typeof(uint);
}
