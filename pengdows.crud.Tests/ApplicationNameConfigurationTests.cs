using System.Data.Common;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class ApplicationNameConfigurationTests
{
    #region ApplyApplicationName Tests

    [Fact]
    public void ApplyApplicationName_SetsApplicationName_WhenNotPresent()
    {
        var connectionString = "Server=localhost;Database=test";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "MyApp",
            "Application Name");

        Assert.Contains("Application Name=MyApp", result);
    }

    [Fact]
    public void ApplyApplicationName_DoesNotOverride_WhenAlreadySet()
    {
        var connectionString = "Server=localhost;Database=test;Application Name=ExistingApp";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "NewApp",
            "Application Name");

        Assert.Contains("Application Name=ExistingApp", result);
        Assert.DoesNotContain("NewApp", result);
    }

    [Fact]
    public void ApplyApplicationName_ReturnsUnchanged_WhenApplicationNameIsNull()
    {
        var connectionString = "Server=localhost;Database=test";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            null,
            "Application Name");

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyApplicationName_ReturnsUnchanged_WhenApplicationNameIsEmpty()
    {
        var connectionString = "Server=localhost;Database=test";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "",
            "Application Name");

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyApplicationName_ReturnsUnchanged_WhenSettingNameIsNull()
    {
        var connectionString = "Server=localhost;Database=test";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "MyApp",
            null);

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyApplicationName_ReturnsUnchanged_WhenSettingNameIsEmpty()
    {
        var connectionString = "Server=localhost;Database=test";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "MyApp",
            "");

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyApplicationName_ReturnsUnchanged_WhenConnectionStringIsEmpty()
    {
        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            "",
            "MyApp",
            "Application Name");

        Assert.Equal("", result);
    }

    [Fact]
    public void ApplyApplicationName_ReturnsUnchanged_ForRawConnectionString()
    {
        // Raw connection strings like ":memory:" should not be modified
        var connectionString = ":memory:";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "MyApp",
            "Application Name");

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyApplicationName_ReusesProvidedBuilder()
    {
        var connectionString = "Server=localhost;Database=test";
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "MyApp",
            "Application Name",
            builder);

        Assert.Contains("Application Name=MyApp", result);
        // Verify builder was modified
        Assert.Equal("MyApp", builder["Application Name"]);
    }

    [Fact]
    public void ApplyApplicationName_WorksWithComposedTenantName()
    {
        var connectionString = "Server=localhost;Database=test";

        var result = ConnectionPoolingConfiguration.ApplyApplicationName(
            connectionString,
            "core-app:tenant-east",
            "Application Name");

        Assert.Contains("Application Name=core-app:tenant-east", result);
    }

    #endregion
}
