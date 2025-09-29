using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests to ensure MinPoolSize configuration is correctly applied based on database support and connection mode
/// </summary>
public class MinPoolSizeBehaviorTests
{
    /// <summary>
    /// Tests that databases supporting pooling get MinPoolSize=1 for Standard mode
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    public void PoolingSupportingDatabases_StandardMode_SetsMinPoolSizeToOne(SupportedDatabase database)
    {
        // Arrange
        var factory = new fakeDbFactory(database);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        // Act
        using var context = new DatabaseContext(config, factory);
        var connectionString = context.ConnectionString;

        // Assert
        var builder = factory.CreateConnectionStringBuilder();
        builder.ConnectionString = connectionString;

        var dialect = context.Dialect;
        if (!string.IsNullOrEmpty(dialect.MinPoolSizeSettingName))
        {
            Assert.True(builder.ContainsKey(dialect.MinPoolSizeSettingName));
            Assert.Equal("1", builder[dialect.MinPoolSizeSettingName]?.ToString());
        }
    }

    /// <summary>
    /// Tests that databases supporting pooling get MinPoolSize=1 for KeepAlive mode
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    public void PoolingSupportingDatabases_KeepAliveMode_SetsMinPoolSizeToOne(SupportedDatabase database)
    {
        // Arrange
        var factory = new fakeDbFactory(database);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.KeepAlive
        };

        // Act
        using var context = new DatabaseContext(config, factory);
        var connectionString = context.ConnectionString;

        // Assert
        var builder = factory.CreateConnectionStringBuilder();
        builder.ConnectionString = connectionString;

        var dialect = context.Dialect;
        if (!string.IsNullOrEmpty(dialect.MinPoolSizeSettingName))
        {
            Assert.True(builder.ContainsKey(dialect.MinPoolSizeSettingName));
            Assert.Equal("1", builder[dialect.MinPoolSizeSettingName]?.ToString());
        }
    }

    /// <summary>
    /// Tests that databases supporting pooling get MinPoolSize=1 for SingleWriter mode
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    public void PoolingSupportingDatabases_SingleWriterMode_SetsMinPoolSizeToOne(SupportedDatabase database)
    {
        // Arrange
        var factory = new fakeDbFactory(database);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.SingleWriter
        };

        // Act
        using var context = new DatabaseContext(config, factory);
        var connectionString = context.ConnectionString;

        // Assert
        var builder = factory.CreateConnectionStringBuilder();
        builder.ConnectionString = connectionString;

        var dialect = context.Dialect;
        if (!string.IsNullOrEmpty(dialect.MinPoolSizeSettingName))
        {
            Assert.True(builder.ContainsKey(dialect.MinPoolSizeSettingName));
            Assert.Equal("1", builder[dialect.MinPoolSizeSettingName]?.ToString());
        }
    }

    /// <summary>
    /// Tests that databases supporting pooling do NOT get MinPoolSize for SingleConnection mode
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    public void PoolingSupportingDatabases_SingleConnectionMode_DoesNotSetMinPoolSize(SupportedDatabase database)
    {
        // Arrange
        var factory = new fakeDbFactory(database);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.SingleConnection
        };

        // Act
        using var context = new DatabaseContext(config, factory);
        var connectionString = context.ConnectionString;

        // Assert
        var builder = factory.CreateConnectionStringBuilder();
        builder.ConnectionString = connectionString;

        var dialect = context.Dialect;
        if (!string.IsNullOrEmpty(dialect.MinPoolSizeSettingName))
        {
            // Should not contain MinPoolSize for SingleConnection mode
            Assert.False(builder.ContainsKey(dialect.MinPoolSizeSettingName));
        }
    }

    /// <summary>
    /// Tests that databases not supporting external pooling do not get pooling configuration
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Sqlite)] // Microsoft.Data.Sqlite case
    public void NonPoolingSupportingDatabases_DoNotGetPoolingConfiguration(SupportedDatabase database)
    {
        // Arrange
        var factory = new fakeDbFactory(database);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        // Act
        using var context = new DatabaseContext(config, factory);
        var connectionString = context.ConnectionString;

        // Assert
        var builder = factory.CreateConnectionStringBuilder();
        builder.ConnectionString = connectionString;

        var dialect = context.Dialect;

        // For non-pooling databases, should not set any pooling parameters
        if (dialect.PoolingSettingName != null)
        {
            // If we have a pooling setting name but don't support external pooling,
            // we should not have added it to the connection string
            if (!dialect.SupportsExternalPooling)
            {
                // Either should not be present, or should be the original value from connection string
                Assert.False(builder.ContainsKey(dialect.PoolingSettingName));
            }
        }

        if (dialect.MinPoolSizeSettingName != null)
        {
            Assert.False(builder.ContainsKey(dialect.MinPoolSizeSettingName));
        }
    }

    /// <summary>
    /// Tests that explicitly provided MinPoolSize values are preserved
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer, "Min Pool Size=5", "5")]
    [InlineData(SupportedDatabase.PostgreSql, "Minimum Pool Size=10", "10")]
    public void ExplicitMinPoolSize_IsPreserved(SupportedDatabase database, string explicitSetting, string expectedValue)
    {
        // Arrange
        var factory = new fakeDbFactory(database);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;{explicitSetting}",
            DbMode = DbMode.Standard
        };

        // Act
        using var context = new DatabaseContext(config, factory);
        var connectionString = context.ConnectionString;

        // Assert
        var builder = factory.CreateConnectionStringBuilder();
        builder.ConnectionString = connectionString;

        var dialect = context.Dialect;
        if (!string.IsNullOrEmpty(dialect.MinPoolSizeSettingName))
        {
            Assert.True(builder.ContainsKey(dialect.MinPoolSizeSettingName));
            Assert.Equal(expectedValue, builder[dialect.MinPoolSizeSettingName]?.ToString());
        }
    }

    /// <summary>
    /// Tests that pooling disabled in connection string prevents MinPoolSize from being set
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public void PoolingDisabled_PreventsMinPoolSizeConfiguration(SupportedDatabase database)
    {
        // Arrange
        var factory = new fakeDbFactory(database);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;Pooling=false",
            DbMode = DbMode.Standard
        };

        // Act
        using var context = new DatabaseContext(config, factory);
        var connectionString = context.ConnectionString;

        // Assert
        var builder = factory.CreateConnectionStringBuilder();
        builder.ConnectionString = connectionString;

        var dialect = context.Dialect;

        // Pooling should be disabled
        if (!string.IsNullOrEmpty(dialect.PoolingSettingName))
        {
            Assert.True(builder.ContainsKey(dialect.PoolingSettingName));
            Assert.Equal("False", builder[dialect.PoolingSettingName]?.ToString());
        }

        // MinPoolSize should not be set when pooling is disabled
        if (!string.IsNullOrEmpty(dialect.MinPoolSizeSettingName))
        {
            Assert.False(builder.ContainsKey(dialect.MinPoolSizeSettingName));
        }
    }
}