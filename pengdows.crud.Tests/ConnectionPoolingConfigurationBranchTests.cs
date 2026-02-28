using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class ConnectionPoolingConfigurationBranchTests
{
    [Fact]
    public void IsPoolingDisabled_InvalidString_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost;Pooling=maybe"
        };

        Assert.False(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    [Fact]
    public void HasMinPoolSize_NullBuilder_ReturnsFalse()
    {
        Assert.False(ConnectionPoolingConfiguration.HasMinPoolSize(null!));
    }

    [Fact]
    public void ApplyPoolingDefaults_RawConnectionString_ReturnsOriginal()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Data Source"] = ":memory:";

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            ":memory:",
            SupportedDatabase.Sqlite,
            DbMode.Standard,
            true,
            builder: builder);

        Assert.Equal(":memory:", result);
    }

    [Fact]
    public void ApplyPoolingDefaults_ExistingMinPool_DoesNotChange()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost;Pooling=true;Min Pool Size=2"
        };

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            builder.ConnectionString,
            SupportedDatabase.SqlServer,
            DbMode.Standard,
            true,
            builder: builder);

        Assert.Equal(builder.ConnectionString, result);
    }

}