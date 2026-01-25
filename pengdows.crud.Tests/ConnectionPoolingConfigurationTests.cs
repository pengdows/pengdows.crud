using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ConnectionPoolingConfigurationTests
{
    #region IsPoolingDisabled Tests

    [Fact]
    public void IsPoolingDisabled_PoolingTrue_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost;Pooling=true"
        };

        var result = ConnectionPoolingConfiguration.IsPoolingDisabled(builder);
        Assert.False(result);
    }

    [Fact]
    public void IsPoolingDisabled_PoolingFalse_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost;Pooling=false"
        };

        var result = ConnectionPoolingConfiguration.IsPoolingDisabled(builder);
        Assert.True(result);
    }

    [Fact]
    public void IsPoolingDisabled_PoolingZero_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost;Pooling=0"
        };

        var result = ConnectionPoolingConfiguration.IsPoolingDisabled(builder);
        Assert.True(result);
    }

    [Fact]
    public void IsPoolingDisabled_PoolingAbsent_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost"
        };

        var result = ConnectionPoolingConfiguration.IsPoolingDisabled(builder);
        Assert.False(result);
    }

    #endregion

    #region HasMinPoolSize Tests

    [Fact]
    public void HasMinPoolSize_MinPoolSize_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Min Pool Size"] = 5;

        var result = ConnectionPoolingConfiguration.HasMinPoolSize(builder);
        Assert.True(result);
    }

    [Fact]
    public void HasMinPoolSize_MinPoolSizeNoCasing_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder();
        builder["MinPoolSize"] = 5;

        var result = ConnectionPoolingConfiguration.HasMinPoolSize(builder);
        Assert.True(result);
    }

    [Fact]
    public void HasMinPoolSize_MinimumPoolSize_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Minimum Pool Size"] = 5;

        var result = ConnectionPoolingConfiguration.HasMinPoolSize(builder);
        Assert.True(result);
    }

    [Fact]
    public void HasMinPoolSize_NoMinPool_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = "Server=localhost"
        };

        var result = ConnectionPoolingConfiguration.HasMinPoolSize(builder);
        Assert.False(result);
    }

    #endregion

    #region TrySetMinPoolSize Tests

    [Fact]
    public void TrySetMinPoolSize_ValidBuilder_ReturnsTrue()
    {
        var builder = new DbConnectionStringBuilder();

        var result = ConnectionPoolingConfiguration.TrySetMinPoolSize(builder, 5);
        Assert.True(result);
        Assert.True(ConnectionPoolingConfiguration.HasMinPoolSize(builder));
    }

    [Fact]
    public void TrySetMinPoolSize_NullBuilder_ReturnsFalse()
    {
        var result = ConnectionPoolingConfiguration.TrySetMinPoolSize(null, 5);
        Assert.False(result);
    }

    [Fact]
    public void TrySetMinPoolSize_PoolingDisabled_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Pooling"] = false;

        var result = ConnectionPoolingConfiguration.TrySetMinPoolSize(builder, 5);
        Assert.False(result);
    }

    [Fact]
    public void TrySetMinPoolSize_AlreadyHasMinPool_ReturnsFalse()
    {
        var builder = new DbConnectionStringBuilder();
        builder["Min Pool Size"] = 10;

        var result = ConnectionPoolingConfiguration.TrySetMinPoolSize(builder, 5);
        Assert.False(result);
        // Original value should be preserved
        Assert.Equal("10", builder["Min Pool Size"].ToString());
    }

    #endregion

    #region ApplyPoolingDefaults Tests

    [Fact]
    public void ApplyPoolingDefaults_StandardMode_AddsPoolingSettings()
    {
        var connectionString = "Server=localhost;Database=test";
        var product = SupportedDatabase.SqlServer;
        var mode = DbMode.Standard;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        Assert.NotEqual(connectionString, result);
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.True(builder.ContainsKey("Pooling"));
        Assert.True(ConnectionPoolingConfiguration.HasMinPoolSize(builder));
    }

    [Fact]
    public void ApplyPoolingDefaults_KeepAliveMode_AddsPoolingSettings()
    {
        var connectionString = "Server=localhost;Database=test";
        var product = SupportedDatabase.PostgreSql;
        var mode = DbMode.KeepAlive;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        Assert.NotEqual(connectionString, result);
    }

    [Fact]
    public void ApplyPoolingDefaults_SingleConnectionMode_NoChanges()
    {
        var connectionString = "Server=localhost;Database=test";
        var product = SupportedDatabase.Sqlite;
        var mode = DbMode.SingleConnection;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyPoolingDefaults_SingleWriterMode_NoChanges()
    {
        var connectionString = "Server=localhost;Database=test";
        var product = SupportedDatabase.Sqlite;
        var mode = DbMode.SingleWriter;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyPoolingDefaults_NoExternalPooling_NoChanges()
    {
        var connectionString = "Server=localhost;Database=test";
        var product = SupportedDatabase.Sqlite;
        var mode = DbMode.Standard;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, false);

        Assert.Equal(connectionString, result);
    }

    [Fact]
    public void ApplyPoolingDefaults_AlreadyHasPooling_PreservesExisting()
    {
        var connectionString = "Server=localhost;Database=test;Pooling=true;Min Pool Size=10";
        var product = SupportedDatabase.SqlServer;
        var mode = DbMode.Standard;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("10", builder["Min Pool Size"].ToString());
    }

    [Fact]
    public void ApplyPoolingDefaults_PoolingDisabled_NoMinPool()
    {
        var connectionString = "Server=localhost;Database=test;Pooling=false";
        var product = SupportedDatabase.PostgreSql;
        var mode = DbMode.Standard;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.False(ConnectionPoolingConfiguration.HasMinPoolSize(builder));
    }

    [Fact]
    public void ApplyPoolingDefaults_RawConnectionString_NoChanges()
    {
        // Raw connection strings like ":memory:" or file paths should not be modified
        var connectionString = ":memory:";
        var product = SupportedDatabase.Sqlite;
        var mode = DbMode.Standard;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        Assert.Equal(connectionString, result);
    }

    #endregion

    #region GetDefaultMinPoolSize Tests

    [Fact]
    public void GetDefaultMinPoolSize_ReturnsOne()
    {
        var result = ConnectionPoolingConfiguration.DefaultMinPoolSize;
        Assert.Equal(1, result);
    }

    #endregion
}