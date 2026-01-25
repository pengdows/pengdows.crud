#region

using System.Collections.Generic;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DialectTheoryTests
{
    public static IEnumerable<object[]> DialectMatrix()
    {
        return new List<object[]>
        {
            new object[] { SupportedDatabase.SqlServer, ProcWrappingStyle.Exec, "@", true },
            new object[] { SupportedDatabase.PostgreSql, ProcWrappingStyle.PostgreSQL, ":", true },
            new object[] { SupportedDatabase.CockroachDb, ProcWrappingStyle.PostgreSQL, ":", true },
            new object[] { SupportedDatabase.MySql, ProcWrappingStyle.Call, "@", true },
            new object[] { SupportedDatabase.MariaDb, ProcWrappingStyle.Call, "@", true },
            new object[] { SupportedDatabase.Firebird, ProcWrappingStyle.ExecuteProcedure, "@", true },
            new object[] { SupportedDatabase.Oracle, ProcWrappingStyle.Oracle, ":", true },
            new object[] { SupportedDatabase.Sqlite, ProcWrappingStyle.None, "@", true },
            new object[] { SupportedDatabase.DuckDB, ProcWrappingStyle.None, "$", true }
        };
    }

    [Theory]
    [MemberData(nameof(DialectMatrix))]
    public void Dialect_CoreProperties_AreExpected(
        SupportedDatabase product,
        ProcWrappingStyle expectedWrap,
        string expectedMarker,
        bool expectedNamed)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(product));
        var info = ctx.DataSourceInfo;

        Assert.Equal(expectedWrap, info.ProcWrappingStyle);
        Assert.Equal(expectedMarker, info.ParameterMarker);
        Assert.Equal(expectedNamed, info.SupportsNamedParameters);

        // Sanity: product name is populated
        Assert.False(string.IsNullOrWhiteSpace(info.DatabaseProductName));
    }
}