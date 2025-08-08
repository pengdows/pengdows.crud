#region

using System;
using pengdow.crud.enums;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

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
    }
}