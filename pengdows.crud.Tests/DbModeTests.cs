#region

using System;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DbModeTests
{
    [Theory]
    [InlineData("KeepAlive", DbMode.KeepAlive)]
    [InlineData("SingleConnection", DbMode.SingleConnection)]
    [InlineData("SingleWriter", DbMode.SingleWriter)]
    [InlineData("Standard", DbMode.Standard)]
    public void EnumParse_ShouldReturnCorrectValue(string input, DbMode expected)
    {
        var result = Enum.Parse<DbMode>(input, true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DbModeEnumParse_InvalidValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<DbMode>("NotADbMode"));
    }

    [Fact]
    public void DbMode_ShouldContainExpectedValues()
    {
        var names = Enum.GetNames(typeof(DbMode));
        Assert.Equal(new[] { "Standard", "KeepAlive", "SingleWriter", "SingleConnection" }, names);
    }
}