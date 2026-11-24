using System;
using System.Linq.Expressions;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Review finding (Ring 1): a float read into a decimal went through Convert.ToDecimal(float), which keeps only 7
/// significant digits, so 3.1415927f came back as 3.141593 with no warning. The float's own shortest round-trip text
/// is the digits it carries; every route into a decimal must keep them.
/// </summary>
public class FloatToDecimalDigitsTests
{
    private const float Pi = 3.1415927f;

    [Fact]
    public void DecimalCoercion_Float_KeepsTheDigitsTheFloatCarries()
    {
        var src = new DbValue(Pi, typeof(float));

        Assert.True(new DecimalCoercion().TryRead(in src, out var value));
        Assert.Equal(3.1415927m, value);
    }

    [Fact]
    public void TypeCoercionHelper_Float_IntoDecimal_KeepsTheDigitsTheFloatCarries()
    {
        var result = TypeCoercionHelper.Coerce(Pi, typeof(float), typeof(decimal), TypeCoercionOptions.Default);

        Assert.Equal(3.1415927m, result);
    }

    [Fact]
    public void ConverterResolution_Float_IntoDecimal_KeepsTheDigitsTheFloatCarries()
    {
        var coerce = ConverterResolution.ResolveCoercer(typeof(float), typeof(decimal), EnumParseFailureMode.Throw, null);

        Assert.Equal(3.1415927m, coerce(Pi));
    }

    [Fact]
    public void ConvertWithCache_Float_IntoDecimal_KeepsTheDigitsTheFloatCarries()
    {
        Assert.Equal(3.1415927m, ConverterResolution.ConvertWithCache(Pi, typeof(decimal)));
    }

    [Fact]
    public void CompiledNumericRead_Float_IntoDecimal_KeepsTheDigitsTheFloatCarries()
    {
        var param = Expression.Parameter(typeof(float), "v");
        var read = Expression.Lambda<Func<float, decimal>>(
            NumericExpressions.Convert(param, typeof(float), typeof(decimal)), param).Compile();

        Assert.Equal(3.1415927m, read(Pi));
    }
}
