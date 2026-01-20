using System;
using System.Reflection;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextInitializationBranchTests
{
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=:memory:", "Isolated")]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=file:memdb1?mode=memory&cache=shared", "Shared")]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=test.db", "None")]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=:memory:;cache=shared", "Shared")]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=:memory:", "Isolated")]
    [InlineData(SupportedDatabase.DuckDB, "Data Source=test.duckdb", "None")]
    [InlineData(SupportedDatabase.PostgreSql, "Data Source=:memory:", "None")]
    public void DetectInMemoryKind_HandlesProviders(SupportedDatabase product, string connectionString, string expected)
    {
        var method = typeof(DatabaseContext).GetMethod(
            "DetectInMemoryKind",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, new object?[] { product, connectionString });

        Assert.NotNull(result);
        Assert.Equal(expected, result!.ToString());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("emulatedproduct=unknown", true)]
    [InlineData("EmulatedProduct=Unknown;Other=Value", true)]
    [InlineData("emulatedproduct=postgres", false)]
    public void IsEmulatedUnknown_DetectsMarker(string? connectionString, bool expected)
    {
        var method = typeof(DatabaseContext).GetMethod(
            "IsEmulatedUnknown",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = (bool)method!.Invoke(null, new object?[] { connectionString })!;

        Assert.Equal(expected, result);
    }
}
