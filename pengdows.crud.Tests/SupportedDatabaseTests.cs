#region

using System;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SupportedDatabaseTests
{
    [Theory]
    [InlineData("CockroachDb", SupportedDatabase.CockroachDb)]
    [InlineData("Firebird", SupportedDatabase.Firebird)]
    [InlineData("MariaDb", SupportedDatabase.MariaDb)]
    [InlineData("MySql", SupportedDatabase.MySql)]
    [InlineData("Oracle", SupportedDatabase.Oracle)]
    [InlineData("PostgreSql", SupportedDatabase.PostgreSql)]
    [InlineData("Sqlite", SupportedDatabase.Sqlite)]
    [InlineData("SqlServer", SupportedDatabase.SqlServer)]
    [InlineData("Unknown", SupportedDatabase.Unknown)]
    public void EnumParse_ShouldReturnCorrectValue(string input, SupportedDatabase expected)
    {
        var result = Enum.Parse<SupportedDatabase>(input, ignoreCase: true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SupportedDatabaseEnumParse_InvalidValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<SupportedDatabase>("NotASupportedDatabase"));
    }

    [Fact]
    public void SupportedDatabase_ShouldContainExpectedValues()
    {
        var names = Enum.GetNames(typeof(SupportedDatabase));
        Assert.Equal(
            new[]
            {
                "Unknown",
                "PostgreSql",
                "SqlServer",
                "Oracle",
                "Firebird",
                "CockroachDb",
                "MariaDb",
                "MySql",
                "Sqlite"
            },
            names);
    }
}