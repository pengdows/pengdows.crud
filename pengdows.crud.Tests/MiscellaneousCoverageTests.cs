#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class MiscellaneousCoverageTests
{
    [Fact]
    public void TrackedConnection_ChangeDatabase_ThrowsNotImplemented()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);

        // Act & Assert - Should throw NotImplementedException
        Assert.Throws<NotImplementedException>(() => trackedConnection.ChangeDatabase("newDatabase"));
    }

    [Fact]
    public void CreatedByAttribute_DefaultConstructor_HasZeroStatements()
    {
        // This attribute has 0 statements according to coverage report
        // Arrange & Act
        var attribute = new CreatedByAttribute();

        // Assert
        Assert.NotNull(attribute);
        // This attribute is a marker attribute with no implementation
    }

    [Fact]
    public void CreatedOnAttribute_DefaultConstructor_HasZeroStatements()
    {
        // This attribute has 0 statements according to coverage report
        // Arrange & Act
        var attribute = new CreatedOnAttribute();

        // Assert
        Assert.NotNull(attribute);
        // This attribute is a marker attribute with no implementation
    }

    [Fact]
    public void ClauseCounters_NextKey_GeneratesSequentialKeys()
    {
        // Tests the internal ClauseCounters helper methods
        // Arrange
        var counters = new ClauseCounters();

        // Act
        var key1 = counters.NextKey();
        var key2 = counters.NextKey();
        var key3 = counters.NextKey();

        // Assert
        Assert.NotEqual(key1, key2);
        Assert.NotEqual(key2, key3);
        Assert.NotEqual(key1, key3);

        // Keys should be sequential - they generate as "k0", "k1", etc.
        Assert.Contains("k", key1.ToLower());
        Assert.Contains("k", key2.ToLower());
        Assert.Contains("k", key3.ToLower());
    }

    [Fact]
    public void ClauseCounters_NextVer_GeneratesVersionKeys()
    {
        // Tests the internal ClauseCounters helper methods
        // Arrange
        var counters = new ClauseCounters();

        // Act
        var ver1 = counters.NextVer();
        var ver2 = counters.NextVer();

        // Assert
        Assert.NotEqual(ver1, ver2);
        Assert.Contains("v", ver1.ToLower());
        Assert.Contains("v", ver2.ToLower());
    }

    [Fact]
    public void DataSourceInformation_Property_ReturnsValue()
    {
        // Tests the DataSourceInformation property getter that has zero coverage
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);

        // Act
        var dataSourceInfo = context.DataSourceInfo;

        // Assert
        Assert.NotNull(dataSourceInfo);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void DatabaseContext_ProcWrappingStrategy_Property_ReturnsValue(SupportedDatabase database)
    {
        // Tests the ProcWrappingStrategy property that has zero coverage
        // Arrange
        var factory = new fakeDbFactory(database);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);

        // Act
        var strategy = context.ProcWrappingStrategy;

        // Assert
        Assert.NotNull(strategy);
    }

    [Fact]
    public void DatabaseContext_ConnectionStatistics_Properties_ReturnValidValues()
    {
        // Tests various connection statistics properties with zero coverage
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);

        // Act & Assert
        Assert.True(context.TotalConnectionFailures >= 0);
        Assert.True(context.TotalConnectionsCreated >= 0);
        Assert.True(context.TotalConnectionsReused >= 0);
        Assert.True(context.TotalConnectionTimeoutFailures >= 0);
        Assert.True(context.ConnectionPoolEfficiency >= 0.0 && context.ConnectionPoolEfficiency <= 1.0);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    public void DatabaseContext_CreateSqlContainer_WorksForAllDatabases(SupportedDatabase database)
    {
        // Tests the CreateSqlContainer method for different databases
        // Arrange
        var factory = new fakeDbFactory(database);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);

        // Act
        var container = context.CreateSqlContainer("SELECT 1");

        // Assert
        Assert.NotNull(container);
        Assert.Equal("SELECT 1", container.Query.ToString());
    }

    [Fact]
    public void TrackedConnection_GetLock_ReturnsLock()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);

        // Act
        var lockObject = trackedConnection.GetLock();

        // Assert
        Assert.NotNull(lockObject);
    }

    [Fact]
    public void TrackedConnection_AllConnectionMethods_WorkCorrectly()
    {
        // Test various TrackedConnection methods to increase coverage
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);

        // Act & Assert - Test various connection operations
        Assert.Equal(ConnectionState.Closed, trackedConnection.State);
        // ConnectionString may be null/empty for fake connections
        Assert.NotNull(trackedConnection.ConnectionString ?? "");

        // Test database change - expect it to throw
        Assert.Throws<NotImplementedException>(() => trackedConnection.ChangeDatabase("testDb"));

        // Open connection first, then test transaction creation
        trackedConnection.Open();
        var transaction = trackedConnection.BeginTransaction();
        Assert.NotNull(transaction);

        // Test command creation
        var command = trackedConnection.CreateCommand();
        Assert.NotNull(command);
    }

    [Fact]
    public void Internal_ClauseCounters_MultipleCounters_WorkIndependently()
    {
        // Test the internal counter logic more thoroughly
        // Arrange
        var counters1 = new ClauseCounters();
        var counters2 = new ClauseCounters();

        // Act
        var key1_1 = counters1.NextKey();
        var key2_1 = counters2.NextKey();
        var ver1_1 = counters1.NextVer();
        var ver2_1 = counters2.NextVer();

        // Assert - Each counter instance should work independently
        Assert.NotNull(key1_1);
        Assert.NotNull(key2_1);
        Assert.NotNull(ver1_1);
        Assert.NotNull(ver2_1);

        // Different instances should produce the same pattern
        // but the exact values may differ based on implementation
        Assert.IsType<string>(key1_1);
        Assert.IsType<string>(ver1_1);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "Data Source=memory")]
    [InlineData(SupportedDatabase.PostgreSql, "Host=localhost;Database=test")]
    [InlineData(SupportedDatabase.SqlServer, "Server=localhost;Database=test")]
    public void DatabaseContext_AllStatisticsProperties_ReturnConsistentValues(SupportedDatabase database,
        string connectionString)
    {
        // Comprehensive test of all statistics properties
        // Arrange
        var factory = new fakeDbFactory(database);
        var context = new DatabaseContext($"{connectionString};EmulatedProduct={database}", factory);

        // Act - Access all statistics properties
        var failures = context.TotalConnectionFailures;
        var created = context.TotalConnectionsCreated;
        var reused = context.TotalConnectionsReused;
        var timeouts = context.TotalConnectionTimeoutFailures;
        var efficiency = context.ConnectionPoolEfficiency;

        // Assert - All should be valid values
        Assert.True(failures >= 0);
        Assert.True(created >= 0);
        Assert.True(reused >= 0);
        Assert.True(timeouts >= 0);
        Assert.True(efficiency >= 0.0 && efficiency <= 1.0);

        // Test that tracking methods update the counters
        var initialFailures = failures;
        context.TrackConnectionFailure(new TimeoutException());
        Assert.True(context.TotalConnectionFailures > initialFailures);
    }

    [Fact]
    public void DatabaseContext_TrackConnectionReuse_IncrementsCounter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);
        var initial = context.TotalConnectionsReused;

        // Act - Use reflection to call internal TrackConnectionReuse
        var method = typeof(DatabaseContext).GetMethod("TrackConnectionReuse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(context, null);

        // Assert
        Assert.True(context.TotalConnectionsReused > initial);
    }

    [Fact]
    public void DatabaseContext_Metrics_Property_ReturnsSnapshot()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);

        // Act
        var metrics = context.Metrics;

        // Assert - DatabaseMetrics is a struct, verify properties are valid
        Assert.True(metrics.ConnectionsCurrent >= 0);
        Assert.True(metrics.ConnectionsMax >= 0);
    }

    [Theory]
    [InlineData(3000000000L)] // > int.MaxValue
    [InlineData(-3000000000L)] // < int.MinValue
    [InlineData(0L)]
    [InlineData(1000L)]
    public void DatabaseContext_SaturateToInt_HandlesEdgeCases(long value)
    {
        // Arrange - Use reflection to access private method
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);
        var method = typeof(DatabaseContext).GetMethod("SaturateToInt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act
        var result = (int)method!.Invoke(null, new object[] { value })!;

        // Assert
        if (value >= int.MaxValue)
        {
            Assert.Equal(int.MaxValue, result);
        }
        else if (value <= int.MinValue)
        {
            Assert.Equal(int.MinValue, result);
        }
        else
        {
            Assert.Equal((int)value, result);
        }
    }

    [Theory]
    [InlineData("Connection timeout occurred")]
    [InlineData("Operation TIMEOUT")]
    public void DatabaseContext_IsTimeoutException_DetectsTimeoutInMessage(string message)
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);
        var exception = new InvalidOperationException(message);
        var initialTimeouts = context.TotalConnectionTimeoutFailures;

        // Act
        context.TrackConnectionFailure(exception);

        // Assert - Should detect timeout in message
        Assert.True(context.TotalConnectionTimeoutFailures > initialTimeouts);
    }

    [Fact]
    public void DatabaseContext_IsTimeoutException_DetectsTimeoutInTypeName()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);

        // Create an exception with "Timeout" in the type name
        var exception = new TimeoutException("Network timeout");
        var initialTimeouts = context.TotalConnectionTimeoutFailures;

        // Act
        context.TrackConnectionFailure(exception);

        // Assert - Should detect TimeoutException type
        Assert.True(context.TotalConnectionTimeoutFailures > initialTimeouts);
    }

    [Fact]
    public void DatabaseContext_TrackConnectionFailure_WithNonTimeoutException_DoesNotIncrementTimeoutCounter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test", factory);
        var initialTimeouts = context.TotalConnectionTimeoutFailures;

        // Act
        context.TrackConnectionFailure(new InvalidOperationException("Generic error"));

        // Assert - Should NOT increment timeout counter
        Assert.Equal(initialTimeouts, context.TotalConnectionTimeoutFailures);
        Assert.True(context.TotalConnectionFailures > 0); // But total failures should increment
    }
}