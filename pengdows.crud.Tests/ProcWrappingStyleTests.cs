#region

using System;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ProcWrappingStyleTests
{
    [Theory]
    [InlineData("Call", ProcWrappingStyle.Call)]
    [InlineData("Exec", ProcWrappingStyle.Exec)]
    [InlineData("ExecuteProcedure", ProcWrappingStyle.ExecuteProcedure)]
    [InlineData("None", ProcWrappingStyle.None)]
    [InlineData("Oracle", ProcWrappingStyle.Oracle)]
    [InlineData("PostgreSQL", ProcWrappingStyle.PostgreSQL)]
    public void EnumParse_ShouldReturnCorrectValue(string input, ProcWrappingStyle expected)
    {
        var result = Enum.Parse<ProcWrappingStyle>(input, true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProcWrappingStyleEnumParse_InvalidValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<ProcWrappingStyle>("NotAnProcWrappingStyle"));
    }

    [Fact]
    public void ProcWrappingStyle_ShouldContainExpectedValues()
    {
        var names = Enum.GetNames(typeof(ProcWrappingStyle));
        Assert.Equal(new[] { "None", "Call", "Exec", "PostgreSQL", "Oracle", "ExecuteProcedure" }, names);
    }

}