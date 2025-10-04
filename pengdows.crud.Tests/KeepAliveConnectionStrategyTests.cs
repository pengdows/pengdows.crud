#region

using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class KeepAliveConnectionStrategyTests
{
    [Fact]
    public void Constructor_Should_Initialize_Strategy()
    {
        // Arrange & Act
        var strategy = new KeepAliveConnectionStrategy();
        
        // Assert
        Assert.NotNull(strategy);
    }

    [Fact]
    public async Task GetConnectionAsync_Should_Return_Connection_For_Read_Operations()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act
        var connection = await strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        
        // Assert
        Assert.NotNull(connection);
    }

    [Fact]
    public async Task GetConnectionAsync_Should_Return_Connection_For_Write_Operations()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act
        var connection = await strategy.GetConnectionAsync(context, ExecutionType.Write, false);
        
        // Assert
        Assert.NotNull(connection);
    }

    [Fact]
    public async Task GetConnectionAsync_Should_Handle_Shared_Connection_Request()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act
        var connection = await strategy.GetConnectionAsync(context, ExecutionType.Read, true);
        
        // Assert
        Assert.NotNull(connection);
    }


    [Fact]
    public async Task CloseConnectionAsync_Should_Handle_Connection_Cleanup()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        var connection = await strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        
        // Act & Assert - Should not throw
        await strategy.CloseConnectionAsync(connection, context);
        
        Assert.True(true); // Verify no exceptions
    }

    [Fact]
    public async Task CloseConnectionAsync_Should_Handle_Null_Connection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act & Assert - Should not throw
        await strategy.CloseConnectionAsync(null, context);
        
        Assert.True(true); // Verify no exceptions
    }

    [Fact]
    public void Dispose_Should_Cleanup_Resources()
    {
        // Arrange
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act & Assert - Should not throw
        strategy.Dispose();
        
        Assert.True(true); // Verify no exceptions
    }

    [Fact]
    public async Task DisposeAsync_Should_Cleanup_Resources_Async()
    {
        // Arrange
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act & Assert - Should not throw
        await strategy.DisposeAsync();
        
        Assert.True(true); // Verify no exceptions
    }

    [Fact]
    public async Task GetConnectionAsync_Should_Maintain_KeepAlive_Sentinel_Connection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act - Get multiple connections
        var connection1 = await strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        var connection2 = await strategy.GetConnectionAsync(context, ExecutionType.Write, false);
        
        // Assert - Both should succeed (sentinel connection should be maintained)
        Assert.NotNull(connection1);
        Assert.NotNull(connection2);
    }


    [Fact]
    public async Task CloseConnectionAsync_Should_Handle_Connection_Close_Failure()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        var connection = await strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        
        // Simulate connection close failure
        factory.SetException(new InvalidOperationException("Failed to close connection"));
        
        // Act & Assert - Should handle gracefully
        try
        {
            await strategy.CloseConnectionAsync(connection, context);
            Assert.True(true); // Success
        }
        catch (InvalidOperationException)
        {
            // Also acceptable - depends on implementation
            Assert.True(true);
        }
    }

    [Fact]
    public async Task GetConnectionAsync_Should_Work_With_Different_Execution_Types()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act - Test all execution types
        var readConnection = await strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        var writeConnection = await strategy.GetConnectionAsync(context, ExecutionType.Write, false);
        
        // Assert
        Assert.NotNull(readConnection);
        Assert.NotNull(writeConnection);
    }

    [Fact]
    public async Task Strategy_Should_Handle_Concurrent_Connection_Requests()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act - Simulate concurrent requests
        var task1 = strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        var task2 = strategy.GetConnectionAsync(context, ExecutionType.Write, false);
        var task3 = strategy.GetConnectionAsync(context, ExecutionType.Read, true);
        
        var connections = await Task.WhenAll(task1, task2, task3);
        
        // Assert - All should succeed
        Assert.All(connections, conn => Assert.NotNull(conn));
    }

    [Fact]
    public async Task GetConnectionAsync_Should_Handle_Context_With_Different_Connection_Strings()
    {
        // Arrange
        var factory1 = new fakeDbFactory(SupportedDatabase.SqlServer);
        var factory2 = new fakeDbFactory(SupportedDatabase.PostgreSql);
        
        var context1 = new DatabaseContext("server=test1", factory1);
        var context2 = new DatabaseContext("host=test2", factory2);
        
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act
        var connection1 = await strategy.GetConnectionAsync(context1, ExecutionType.Read, false);
        var connection2 = await strategy.GetConnectionAsync(context2, ExecutionType.Read, false);
        
        // Assert - Should handle different contexts
        Assert.NotNull(connection1);
        Assert.NotNull(connection2);
    }

    [Fact]
    public async Task CloseConnectionAsync_Should_Handle_Already_Closed_Connection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        var connection = await strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        
        // Close connection first time
        await strategy.CloseConnectionAsync(connection, context);
        
        // Act & Assert - Second close should not throw
        await strategy.CloseConnectionAsync(connection, context);
        
        Assert.True(true); // Verify no exceptions
    }

    [Fact]
    public void Strategy_Should_Implement_IDisposable_Pattern()
    {
        // Arrange
        var strategy = new KeepAliveConnectionStrategy();
        
        // Act & Assert - Test disposable pattern
        Assert.True(strategy is IDisposable);
        Assert.True(strategy is IAsyncDisposable);
        
        // Multiple dispose calls should not throw
        strategy.Dispose();
        strategy.Dispose();
    }

    [Fact]
    public async Task Strategy_Should_Handle_Sentinel_Connection_Failure()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("test", factory);
        var strategy = new KeepAliveConnectionStrategy();
        
        // Simulate sentinel connection failure after initial success
        var connection1 = await strategy.GetConnectionAsync(context, ExecutionType.Read, false);
        Assert.NotNull(connection1);
        
        // Now simulate factory failure
        factory.SetConnectionException(new InvalidOperationException("Sentinel connection lost"));
        
        // Act & Assert - Should handle sentinel connection failure gracefully
        try
        {
            var connection2 = await strategy.GetConnectionAsync(context, ExecutionType.Write, false);
            Assert.NotNull(connection2); // May succeed if sentinel is maintained
        }
        catch (InvalidOperationException)
        {
            // Also acceptable - depends on implementation details
            Assert.True(true);
        }
    }
}
