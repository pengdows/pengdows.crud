using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

// DRY-005: the one numeric-type check every read path uses (it replaced four copies).
public class NumericTypesTests
{
    public enum Small : byte
    {
        A = 1
    }

    [Theory]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    public void IntegralTypes_AreNumericAndIntegral(Type type)
    {
        Assert.True(NumericTypes.IsNumeric(type));
        Assert.True(NumericTypes.IsIntegral(type));
    }

    [Theory]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(decimal))]
    public void FractionalTypes_AreNumericNotIntegral(Type type)
    {
        Assert.True(NumericTypes.IsNumeric(type));
        Assert.False(NumericTypes.IsIntegral(type));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(char))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(int?))]
    public void OtherTypes_AreNotNumeric(Type type)
    {
        Assert.False(NumericTypes.IsNumeric(type));
        Assert.False(NumericTypes.IsIntegral(type));
    }

    [Fact]
    public void AnEnum_CountsAsItsUnderlyingInteger() =>
        Assert.True(NumericTypes.IsIntegral(typeof(Small)));
}
