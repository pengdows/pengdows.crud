using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;
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
    public void TrySetMinPoolSize_UsesProperty_WhenAvailable()
    {
        var builder = new BuilderWithMinPoolProperty();

        Assert.True(ConnectionPoolingConfiguration.TrySetMinPoolSize(builder, 7));
        Assert.Equal(7, builder.MinPoolSize);
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
            supportsExternalPooling: true,
            builder: builder);

        Assert.Equal(":memory:", result);
    }

    [Fact]
    public void ApplyPoolingDefaults_PoolingDisabled_SkipsMinPool()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost;Pooling=false"
        };

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            builder.ConnectionString,
            SupportedDatabase.SqlServer,
            DbMode.Standard,
            supportsExternalPooling: true,
            builder: builder);

        var parsed = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.True(parsed.ContainsKey("Pooling"));
        Assert.False(ConnectionPoolingConfiguration.HasMinPoolSize(parsed));
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
            supportsExternalPooling: true,
            builder: builder);

        Assert.Equal(builder.ConnectionString, result);
    }

    private sealed class BuilderWithMinPoolProperty : DbConnectionStringBuilder
    {
        public int MinPoolSize { get; set; }
    }
}
