using System;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DatabaseContextConnectionStringValidationTests
{
    [Fact]
    public void Equivalent_WhenOnlyCredentialsDiffer()
    {
        var primary = "Data Source=server;Database=app;User Id=writer;Password=secret;";
        var secondary = "Data Source=server;Database=app;User Id=reader;Password=other;";

        var result = InvokeComparison(primary, secondary, null, null, ":ro");

        Assert.True(result);
    }

    [Fact]
    public void NotEquivalent_WhenDatabaseDiffers()
    {
        var primary = "Data Source=server;Database=app;User Id=writer;Password=secret;";
        var secondary = "Data Source=server;Database=app_ro;User Id=reader;Password=other;";

        var result = InvokeComparison(primary, secondary, null, null, ":ro");

        Assert.False(result);
    }

    [Fact]
    public void Equivalent_WhenReadOnlyParameterAndSuffixDiffer()
    {
        var primary = "Data Source=server;Database=app;Application Name=Widget;User Id=writer;Password=secret;";
        var secondary =
            "Data Source=server;Database=app;Application Name=Widget:ro;ApplicationIntent=ReadOnly;User Id=reader;Password=other;";

        var result = InvokeComparison(primary, secondary, "ApplicationIntent=ReadOnly", "Application Name", ":ro");

        Assert.True(result);
    }

    private static bool InvokeComparison(
        string primary,
        string secondary,
        string? readOnlyParameter,
        string? applicationNameSettingName,
        string suffix)
    {
        var method = typeof(DatabaseContext).GetMethod(
            "AreConnectionStringsEquivalentIgnoringCredentials",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object?[]
        {
            primary,
            secondary,
            readOnlyParameter,
            applicationNameSettingName,
            suffix
        });

        Assert.IsType<bool>(result);
        return (bool)result!;
    }
}
