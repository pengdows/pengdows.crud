// =============================================================================
// FILE: FirebirdDecFloat.cs
// PURPOSE: Exact conversions between .NET numbers and FirebirdClient's FbDecFloat (DECFLOAT),
//          through reflection so the core library carries no provider reference.
//
// AI SUMMARY:
// - FirebirdClient reads DECFLOAT as FirebirdSql.Data.Types.FbDecFloat (not IConvertible) and binds a
//   DECFLOAT parameter only from FbDecFloat (TYPE-005, confirmed live with FirebirdClient 10.3.4).
// - Its own explicit conversion from decimal throws for every integral decimal (1m, 100m), so values
//   are built through the (BigInteger coefficient, int exponent) constructor instead.
// - Reads are exact or throw: NaN/Infinity have no decimal (FormatException), a coefficient that
//   doesn't fit decimal's 96 bits at scale <= 28 throws OverflowException. Doubles get NaN/Infinity.
// =============================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace pengdows.crud.dialects;

internal static class FirebirdDecFloat
{
    private const string TypeName = "FirebirdSql.Data.Types.FbDecFloat";

    private sealed record Accessors(
        PropertyInfo Kind,
        PropertyInfo Negative,
        PropertyInfo Coefficient,
        PropertyInfo Exponent,
        ConstructorInfo Create,
        object PositiveNaN,
        object PositiveInfinity,
        object NegativeInfinity);

    private static readonly ConcurrentDictionary<Type, Accessors?> Cache = new();

    public static bool Is(object? value) => value != null && value.GetType().FullName == TypeName;

    /// <summary>The FbDecFloat type from the provider's assembly, or null when it isn't available.</summary>
    public static Type? Resolve(Assembly providerAssembly) =>
        providerAssembly.GetType(TypeName) ?? Type.GetType($"{TypeName}, FirebirdSql.Data.FirebirdClient");

    public static decimal ToDecimal(object decFloat)
    {
        var a = For(decFloat.GetType());
        var kind = a.Kind.GetValue(decFloat)?.ToString();
        if (kind != "Finite")
        {
            throw new FormatException($"DECFLOAT {kind} has no System.Decimal value.");
        }

        var coefficient = (BigInteger)a.Coefficient.GetValue(decFloat)!;
        var exponent = (int)a.Exponent.GetValue(decFloat)!;
        if (exponent >= 0)
        {
            return (decimal)(coefficient * BigInteger.Pow(10, exponent));
        }

        var scale = -exponent;
        var limit = BigInteger.One << 96;
        while (scale > 28 || BigInteger.Abs(coefficient) >= limit)
        {
            if (scale == 0 || !(coefficient % 10).IsZero)
            {
                throw new OverflowException(
                    $"DECFLOAT {coefficient}E{exponent} is outside System.Decimal's range or precision.");
            }

            coefficient /= 10;
            scale--;
        }

        var magnitude = BigInteger.Abs(coefficient);
        return new decimal(
            (int)(uint)(magnitude & uint.MaxValue),
            (int)(uint)((magnitude >> 32) & uint.MaxValue),
            (int)(uint)(magnitude >> 64),
            coefficient.Sign < 0,
            (byte)scale);
    }

    public static double ToDouble(object decFloat)
    {
        var a = For(decFloat.GetType());
        var negative = (bool)a.Negative.GetValue(decFloat)!;
        return a.Kind.GetValue(decFloat)?.ToString() switch
        {
            "Finite" => double.Parse(
                $"{(BigInteger)a.Coefficient.GetValue(decFloat)!}E{(int)a.Exponent.GetValue(decFloat)!}",
                NumberStyles.Float, CultureInfo.InvariantCulture),
            "Infinity" => negative ? double.NegativeInfinity : double.PositiveInfinity,
            _ => double.NaN
        };
    }

    /// <summary>An FbDecFloat of <paramref name="decFloatType"/> holding <paramref name="value"/> exactly.</summary>
    public static object From(Type decFloatType, object value)
    {
        var a = For(decFloatType);
        switch (value)
        {
            case double d when double.IsNaN(d):
                return a.PositiveNaN;
            case double d when double.IsInfinity(d):
                return d > 0 ? a.PositiveInfinity : a.NegativeInfinity;
            case float f when float.IsNaN(f):
                return a.PositiveNaN;
            case float f when float.IsInfinity(f):
                return f > 0 ? a.PositiveInfinity : a.NegativeInfinity;
        }

        var text = value switch
        {
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
        };

        var exponent = 0;
        var e = text.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..e];
        }

        var point = text.IndexOf('.');
        if (point >= 0)
        {
            exponent -= text.Length - point - 1;
            text = text.Remove(point, 1);
        }

        return a.Create.Invoke([BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), exponent]);
    }

    private static Accessors For(Type type) =>
        Cache.GetOrAdd(type, static t =>
        {
            const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var kind = t.GetProperty("Type", instance);
            var negative = t.GetProperty("Negative", instance);
            var coefficient = t.GetProperty("Coefficient", instance);
            var exponent = t.GetProperty("Exponent", instance);
            var create = t.GetConstructor([typeof(BigInteger), typeof(int)]);
            object? Static(string name) => t.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var nan = Static("PositiveNaN");
            var positiveInfinity = Static("PositiveInfinity");
            var negativeInfinity = Static("NegativeInfinity");
            return kind == null || negative == null || coefficient == null || exponent == null || create == null ||
                   nan == null || positiveInfinity == null || negativeInfinity == null
                ? null
                : new Accessors(kind, negative, coefficient, exponent, create, nan, positiveInfinity, negativeInfinity);
        }) ?? throw new NotSupportedException($"{type.FullName} doesn't have the FbDecFloat shape this version supports.");
}
