using System;
using System.Linq.Expressions;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DRY-010: the expression both compiled mappers read a numeric column with into a numeric, enum or
/// bool property.
/// </summary>
public class NumericExpressionsTests
{
    public enum Level : byte
    {
        Low = 1,
        High = 2
    }

    private static TTarget Read<TSource, TTarget>(TSource value, bool boxed = false)
    {
        var parameter = Expression.Parameter(boxed ? typeof(object) : typeof(TSource));
        var target = Nullable.GetUnderlyingType(typeof(TTarget)) ?? typeof(TTarget);
        Expression body = NumericExpressions.Convert(parameter, typeof(TSource), target);
        if (body.Type != typeof(TTarget))
        {
            body = Expression.Convert(body, typeof(TTarget));
        }

        var input = boxed ? (object?)value : value;
        return (TTarget)Expression.Lambda(body, parameter).Compile().DynamicInvoke(input)!;
    }

    private static void Fails<TSource, TTarget>(TSource value) where TTarget : struct
    {
        var ex = Assert.ThrowsAny<Exception>(() => Read<TSource, TTarget>(value));
        Assert.True(ex.InnerException is OverflowException or InvalidCastException, ex.ToString());
    }

    [Fact]
    public void ConvertsExactly()
    {
        Assert.Equal(7, Read<int, int>(7));
        Assert.Equal(7, Read<long, int>(7L));
        Assert.Equal(3, Read<double, int>(3.0));
        Assert.Equal(1.25m, Read<double, decimal>(1.25));
        Assert.Equal(1.25, Read<decimal, double>(1.25m));
        Assert.Equal(1.5f, Read<decimal, float>(1.5m));
        Assert.Equal(7L, Read<int, long>(7, boxed: true));
        Assert.Equal((int?)7, Read<long, int?>(7L));
        Assert.Equal(Level.High, Read<decimal, Level>(2m));
    }

    [Fact]
    public void ValueThePropertyCantHold_Fails()
    {
        Fails<long, int>(long.MaxValue);
        Fails<int, byte>(-1);
        Fails<double, int>(2.5);
        Fails<decimal, long>(1.1m);
        Fails<double, decimal>(double.NaN);
    }

    [Fact]
    public void IntoBool_NonZeroIsTrue_NaNFails()
    {
        Assert.True(Read<double, bool>(-0.5));
        Assert.False(Read<float, bool>(0f));
        Assert.True(Read<decimal, bool>(3m));
        Assert.False(Read<long, bool>(0L));
        Assert.True(Read<byte, bool?>((byte)1)!.Value);
        Fails<double, bool>(double.NaN);
        Fails<float, bool>(float.NaN);
    }

    [Theory]
    [InlineData(typeof(int), typeof(long), true)]
    [InlineData(typeof(double), typeof(bool), true)]
    [InlineData(typeof(decimal), typeof(Level), true)]
    [InlineData(typeof(string), typeof(int), false)]
    [InlineData(typeof(int), typeof(string), false)]
    [InlineData(typeof(bool), typeof(int), false)]
    public void Handles_NumericSourcesIntoNumericEnumAndBoolTargets(Type source, Type target, bool expected)
    {
        Assert.Equal(expected, NumericExpressions.Handles(source, target));
    }
}
