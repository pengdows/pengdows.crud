using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.configuration;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ExecuteReaderWriteConnectionLeakTests
{
    [Fact]
    public async Task ExecuteReaderAsync_Write_FailureDisposesConnection()
    {
        // Arrange: Standard mode so write connections are ephemeral (not pinned)
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // Init connection for DatabaseContext setup
        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        // Operation connection that will fail on reader execution
        var opConnection = new fakeDbConnection();
        opConnection.SetFailOnCommand();
        opConnection.SetCustomFailureException(new InvalidOperationException("reader failed"));
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        await using var container = context.CreateSqlContainer("SELECT 1");

        // Act: Execute reader with Write execution type — should throw
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.ExecuteReaderAsync(ExecutionType.Write));

        // Assert: The write connection should have been disposed on failure
        Assert.True(opConnection.DisposeCount > 0,
            "Write connection should be disposed when ExecuteReaderAsync fails before TrackedReader creation");
    }

    [Fact]
    public async Task ExecuteReaderAsync_Read_FailureDisposesConnection()
    {
        // Arrange: Standard mode so read connections are ephemeral
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // Init connection for DatabaseContext setup
        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        // Operation connection that will fail on reader execution
        var opConnection = new fakeDbConnection();
        opConnection.SetFailOnCommand();
        opConnection.SetCustomFailureException(new InvalidOperationException("reader failed"));
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        await using var container = context.CreateSqlContainer("SELECT 1");

        // Act: Execute reader with Read execution type — should throw
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.ExecuteReaderAsync(ExecutionType.Read));

        // Assert: The read connection should have been disposed on failure
        // (no TrackedReader was created to take ownership)
        Assert.True(opConnection.DisposeCount > 0,
            "Read connection should be disposed when ExecuteReaderAsync fails before TrackedReader creation");
    }

    [Fact]
    public async Task ExecuteReaderAsync_Write_SuccessDoesNotDisposeConnectionEarly()
    {
        // Arrange: Standard mode
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        var opConnection = new fakeDbConnection();
        opConnection.EnqueueReaderResult(new List<Dictionary<string, object?>>
        {
            new() { ["Value"] = 42 }
        });
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        await using var container = context.CreateSqlContainer("SELECT 1");

        // Act: Execute reader with Write — should succeed
        await using var reader = await container.ExecuteReaderAsync(ExecutionType.Write);

        // Assert: Connection should NOT be disposed while reader is open
        Assert.Equal(0, opConnection.DisposeCount);
    }
}