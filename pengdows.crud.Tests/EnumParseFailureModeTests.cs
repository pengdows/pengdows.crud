#region

using System;
using System.Text.Json;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EnumParseFailureModeTests
{
    [Theory]
    [InlineData("Throw", EnumParseFailureMode.Throw)]
    [InlineData("setdefaultvalue", EnumParseFailureMode.SetDefaultValue)]
    [InlineData("SETNULLANDLOG", EnumParseFailureMode.SetNullAndLog)]
    public void ParseEnumValue_Success(string input, EnumParseFailureMode expected)
    {
        var result = Enum.Parse<EnumParseFailureMode>(input, true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseEnumValue_Invalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<EnumParseFailureMode>("NotAStatus"));
    }

    [Fact]
    public void EnumParseFailureModeEnum_SerializesToString()
    {
        var obj = new { ParseFailureMode = EnumParseFailureMode.SetNullAndLog };
        var json = JsonSerializer.Serialize(obj);
        Assert.Contains("ParseFailureMode", json);
    }
}