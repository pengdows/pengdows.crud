using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
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

    #region ApplyPoolingDefaults Tests

    [Fact]
    public void ApplyPoolingDefaults_StandardMode_AddsPoolingSettingOnly()
    {
        var connectionString = "Server=localhost;Database=test";
        var product = SupportedDatabase.SqlServer;
        var mode = DbMode.Standard;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        Assert.NotEqual(connectionString, result);
        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.True(builder.ContainsKey("Pooling"));
        Assert.False(ConnectionPoolingConfiguration.HasMinPoolSize(builder));
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
    public void ApplyPoolingDefaults_SingleWriterMode_AppliesDefaults()
    {
        var connectionString = "Server=localhost;Database=test";
        var product = SupportedDatabase.Sqlite;
        var mode = DbMode.SingleWriter;

        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            connectionString, product, mode, true);

        // SingleWriter uses Standard lifecycle with provider pooling — defaults are applied
        Assert.NotEqual(connectionString, result);
        Assert.Contains("Pooling", result);
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
    public void ApplyPoolingDefaults_PoolingDisabledExplicitly_ThrowsInvalidOperationException()
    {
        var connectionString = "Server=localhost;Database=test;Pooling=false";

        Assert.Throws<InvalidOperationException>(() =>
            ConnectionPoolingConfiguration.ApplyPoolingDefaults(
                connectionString, SupportedDatabase.PostgreSql, DbMode.Standard, true));
    }

    [Fact]
    public void ApplyPoolingDefaults_PoolingDisabled_KeepAlive_ThrowsInvalidOperationException()
    {
        var connectionString = "Server=localhost;Database=test;Pooling=false";

        Assert.Throws<InvalidOperationException>(() =>
            ConnectionPoolingConfiguration.ApplyPoolingDefaults(
                connectionString, SupportedDatabase.SqlServer, DbMode.KeepAlive, true));
    }

    [Fact]
    public void ApplyPoolingDefaults_PoolingDisabled_SingleWriter_ThrowsInvalidOperationException()
    {
        var connectionString = "Server=localhost;Database=test;Pooling=false";

        Assert.Throws<InvalidOperationException>(() =>
            ConnectionPoolingConfiguration.ApplyPoolingDefaults(
                connectionString, SupportedDatabase.SqlServer, DbMode.SingleWriter, true));
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

    #region ApplyMaxPoolSize Tests

    [Fact]
    public void ApplyMaxPoolSize_AddsValueWhenMissing()
    {
        var connectionString = "Server=localhost";

        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            connectionString,
            20,
            "Max Pool Size");

        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("20", builder["Max Pool Size"].ToString());
    }

    [Fact]
    public void ApplyMaxPoolSize_OverrideExisting_ReplacesValue()
    {
        var connectionString = "Server=localhost;Max Pool Size=5";

        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            connectionString,
            10,
            "Max Pool Size",
            overrideExisting: true);

        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal("10", builder["Max Pool Size"].ToString());
    }

    [Fact]
    public void ApplyMaxPoolSize_RawConnectionString_NoChanges()
    {
        var connectionString = ":memory:";

        var result = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
            connectionString,
            5,
            "Max Pool Size");

        Assert.Equal(connectionString, result);
    }

    #endregion

    #region ClampMinPoolSize Tests

    [Fact]
    public void ClampMinPoolSize_Negative_ClampsToZero()
    {
        var result = ConnectionPoolingConfiguration.ClampMinPoolSize(
            "Server=localhost;Min Pool Size=-5;Max Pool Size=10",
            "Min Pool Size", rawMin: -5, rawMax: 10);

        var b = new DbConnectionStringBuilder { ConnectionString = result };
        b.TryGetValue("Min Pool Size", out var v);
        Assert.Equal("0", v?.ToString());
    }

    [Fact]
    public void ClampMinPoolSize_ExceedsMax_ClampsToMax()
    {
        var result = ConnectionPoolingConfiguration.ClampMinPoolSize(
            "Server=localhost;Min Pool Size=20;Max Pool Size=5",
            "Min Pool Size", rawMin: 20, rawMax: 5);

        var b = new DbConnectionStringBuilder { ConnectionString = result };
        b.TryGetValue("Min Pool Size", out var v);
        Assert.Equal("5", v?.ToString());
    }

    [Fact]
    public void ClampMinPoolSize_AlreadyValid_ReturnsOriginal()
    {
        var original = "Server=localhost;Min Pool Size=3;Max Pool Size=10";

        var result = ConnectionPoolingConfiguration.ClampMinPoolSize(
            original, "Min Pool Size", rawMin: 3, rawMax: 10);

        Assert.Same(original, result);
    }

    [Fact]
    public void ClampMinPoolSize_NullMin_ReturnsOriginal()
    {
        var original = "Server=localhost;Max Pool Size=10";

        var result = ConnectionPoolingConfiguration.ClampMinPoolSize(
            original, "Min Pool Size", rawMin: null, rawMax: 10);

        Assert.Same(original, result);
    }

    [Fact]
    public void ClampMinPoolSize_NullMax_ClampsNegativeToZeroOnly()
    {
        // No max known → clamp to 0 but no upper bound
        var result = ConnectionPoolingConfiguration.ClampMinPoolSize(
            "Server=localhost;Min Pool Size=-3",
            "Min Pool Size", rawMin: -3, rawMax: null);

        var b = new DbConnectionStringBuilder { ConnectionString = result };
        b.TryGetValue("Min Pool Size", out var v);
        Assert.Equal("0", v?.ToString());
    }

    [Fact]
    public void ClampMinPoolSize_ZeroMax_ClampsPositiveMinToZero()
    {
        // Forbidden pool (max=0): any positive min gets clamped to 0
        var result = ConnectionPoolingConfiguration.ClampMinPoolSize(
            "Server=localhost;Min Pool Size=5;Max Pool Size=0",
            "Min Pool Size", rawMin: 5, rawMax: 0);

        var b = new DbConnectionStringBuilder { ConnectionString = result };
        b.TryGetValue("Min Pool Size", out var v);
        Assert.Equal("0", v?.ToString());
    }

    #endregion
}