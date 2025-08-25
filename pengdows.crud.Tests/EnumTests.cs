#region

using System;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EnumTests
{
    [Fact]
    public void EnumParseFailureMode_Default_IsThrow()
    {
        Assert.Equal(EnumParseFailureMode.Throw, EnumParseFailureMode.Throw);
    }

    [Fact]
    public void SupportedDatabase_ContainsExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(SupportedDatabase), SupportedDatabase.PostgreSql));
        Assert.True(Enum.IsDefined(typeof(SupportedDatabase), SupportedDatabase.DuckDB));
    }
}