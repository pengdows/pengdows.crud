using System.Globalization;
using System.Reflection;

namespace pengdows.crud.@internal;

/// <summary>
/// A fractional value read into an integer must be whole (COR-009): 2.7 is neither 2 nor 3, so it
/// fails like an overflow does (TYPE-008) instead of being truncated or rounded. NaN fails here;
/// infinity passes and overflows in the checked conversion that follows.
/// </summary>
internal static class WholeNumber
{
    public static decimal Require(decimal value) => decimal.Truncate(value) == value ? value : throw Fraction(value);

    public static double Require(double value) => Math.Truncate(value) == value ? value : throw Fraction(value);

    public static float Require(float value) => MathF.Truncate(value) == value ? value : throw Fraction(value);

    /// <summary>The Require overload for a fractional <paramref name="source"/> read into an integral <paramref name="target"/>, or null.</summary>
    public static MethodInfo? RequireFor(Type source, Type target) =>
        IsIntegral(target) && (source == typeof(decimal) || source == typeof(double) || source == typeof(float))
            ? typeof(WholeNumber).GetMethod(nameof(Require), new[] { source })
            : null;

    /// <summary>Throws when <paramref name="value"/> is a fractional number <paramref name="target"/> (integral) can't hold exactly.</summary>
    public static void Check(object value, Type target)
    {
        if (!IsIntegral(target))
        {
            return;
        }

        switch (value)
        {
            case decimal m:
                Require(m);
                break;
            case double d:
                Require(d);
                break;
            case float f:
                Require(f);
                break;
        }
    }

    private static bool IsIntegral(Type type) => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
        or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64
        or TypeCode.UInt64 && !type.IsEnum;

    private static InvalidCastException Fraction(IFormattable value) =>
        new($"{value.ToString(null, CultureInfo.InvariantCulture)} has a fractional part; an integer can't hold it exactly.");
}
