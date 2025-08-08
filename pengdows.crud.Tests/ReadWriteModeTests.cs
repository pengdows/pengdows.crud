#region

using System;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ReadWriteModeTests
{
    [Theory]
    [InlineData("ReadOnly", ReadWriteMode.ReadOnly)]
    [InlineData("ReadWrite", ReadWriteMode.ReadWrite)]
    [InlineData("WriteOnly", ReadWriteMode.WriteOnly)]
    public void EnumParse_ShouldReturnCorrectValue(string input, ReadWriteMode expected)
    {
        var result = Enum.Parse<ReadWriteMode>(input, true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadWriteModeEnumParse_InvalidValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<ReadWriteMode>("NotAReadWriteMode"));
    }

    [Fact]
    public void ReadWriteMode_ShouldContainExpectedValues()
    {
        var names = Enum.GetNames(typeof(ReadWriteMode));
        Assert.Equal(new[] { "ReadOnly", "WriteOnly", "ReadWrite" }, names);
    }
}