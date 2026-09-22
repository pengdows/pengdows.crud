#region

using System;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SupportedDatabaseTests
{
    [Theory]
    [InlineData("Snowflake", SupportedDatabase.Snowflake)]
    [InlineData("CockroachDb", SupportedDatabase.CockroachDb)]
    [InlineData("Firebird", SupportedDatabase.Firebird)]
    [InlineData("MariaDb", SupportedDatabase.MariaDb)]
    [InlineData("MySql", SupportedDatabase.MySql)]
    [InlineData("Oracle", SupportedDatabase.Oracle)]
    [InlineData("PostgreSql", SupportedDatabase.PostgreSql)]
    [InlineData("Sqlite", SupportedDatabase.Sqlite)]
    [InlineData("SqlServer", SupportedDatabase.SqlServer)]
    [InlineData("DuckDB", SupportedDatabase.DuckDB)]
    [InlineData("Unknown", SupportedDatabase.Unknown)]
    public void EnumParse_ShouldReturnCorrectValue(string input, SupportedDatabase expected)
    {
        var result = Enum.Parse<SupportedDatabase>(input, true);
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
                "Sqlite",
                "DuckDB",
                "YugabyteDb",
                "TiDb",
                "Snowflake",
                "AuroraMySql",
                "AuroraPostgreSql",
                "Db2",
                "FlatFile",
                "SingleStore",
                "Sybase"
            },
            names);
    }

    /// <summary>
    /// Db2/FlatFile/SingleStore/Sybase were added independently on 2.0.6, 2.1.0, and 3.0, each in
    /// a different order — leaving the same enum member with a different numeric bit value on
    /// each branch (e.g. Sybase was 65536 here but 131072 on 3.0). Since this is a [Flags]-style
    /// enum, that divergence is a real cross-branch binary-compatibility hazard for anything that
    /// persists, logs, or compares the numeric value. Realigned to match 3.0's canonical values
    /// (the branch these are permanently pinned against, per the 2.0.6/2.1.0/3.0 compatibility
    /// policy) since neither 2.0.6 nor 2.1.0 had ever shipped a tagged release with the old
    /// values. Sybase was also renamed from bare Sybase to match 3.0's member name exactly
    /// (same bit value throughout — this was a naming-only divergence, not a value collision) —
    /// disambiguates from Sybase IQ, a distinct product this dialect does not target.
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.Db2, 16384)]
    [InlineData(SupportedDatabase.FlatFile, 32768)]
    [InlineData(SupportedDatabase.SingleStore, 65536)]
    [InlineData(SupportedDatabase.Sybase, 131072)]
    public void SupportedDatabase_LateAddedMembers_MatchCanonical3_0Values(SupportedDatabase value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }
}