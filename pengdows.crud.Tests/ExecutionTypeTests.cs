#region

using System;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ExecutionTypeTests
{
    [Theory]
    [InlineData("Read", ExecutionType.Read)]
    [InlineData("Write", ExecutionType.Write)]
    public void EnumParse_ShouldReturnCorrectValue(string input, ExecutionType expected)
    {
        var result = Enum.Parse<ExecutionType>(input, true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExecutionTypeEnumParse_InvalidValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<ExecutionType>("NotAnExecutionType"));
    }

    [Fact]
    public void ExecutionType_ShouldContainExpectedValues()
    {
        var names = Enum.GetNames(typeof(ExecutionType));
        Assert.Equal(new[] { "Read", "Write" }, names);
    }
}