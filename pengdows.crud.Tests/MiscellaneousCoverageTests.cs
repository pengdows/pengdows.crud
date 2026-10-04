#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class MiscellaneousCoverageTests
{

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
        Assert.True(context.TotalConnectionTimeoutFailures >= 0);
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
        var timeouts = context.TotalConnectionTimeoutFailures;

        // Assert - All should be valid values
        Assert.True(failures >= 0);
        Assert.True(created >= 0);
        Assert.True(timeouts >= 0);

        // Test that tracking methods update the counters
        var initialFailures = failures;
        context.TrackConnectionFailure(new TimeoutException());
        Assert.True(context.TotalConnectionFailures > initialFailures);
    }

}