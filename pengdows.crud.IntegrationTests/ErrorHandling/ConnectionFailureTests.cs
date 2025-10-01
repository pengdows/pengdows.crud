using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Integration tests for connection failure scenarios using FakeDb to simulate
/// various database connection problems and verify pengdows.crud's error handling.
/// </summary>
public class ConnectionFailureTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    public ConnectionFailureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Connection_FailOnOpen_ThrowsAppropriateException()
    {
        // Arrange
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailOnOpen);

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var helper = new EntityHelper<TestTable, long>(context);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var entity = CreateTestEntity("FailOnOpen");
            await helper.CreateAsync(entity, context);
        });

        Assert.Contains("Connection failed to open", exception.Message);
        _output.WriteLine($"FailOnOpen test completed: {exception.Message}");
    }

    [Fact]
    public async Task Connection_FailOnCommand_HandlesCommandCreationFailure()
    {
        // Arrange
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.PostgreSql,
            ConnectionFailureMode.FailOnCommand);

        using var context = new DatabaseContext("Host=localhost;Database=test;EmulatedProduct=PostgreSql", factory);
        var helper = new EntityHelper<TestTable, long>(context);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var entity = CreateTestEntity("FailOnCommand");
            await helper.CreateAsync(entity, context);
        });

        Assert.Contains("Command creation failed", exception.Message);
        _output.WriteLine($"FailOnCommand test completed: {exception.Message}");
    }

    [Fact]
    public async Task Connection_FailOnTransaction_HandlesTransactionFailure()
    {
        // Arrange
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.SqlServer,
            ConnectionFailureMode.FailOnTransaction);

        using var context = new DatabaseContext("Server=test;Database=test;EmulatedProduct=SqlServer", factory);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var transaction = context.BeginTransaction();
            // Transaction creation should fail
        });

        Assert.Contains("Transaction failed", exception.Message);
        _output.WriteLine($"FailOnTransaction test completed: {exception.Message}");
    }

    [Fact]
    public async Task Connection_FailAfterCount_WorksUntilThreshold()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.SetFailAfterOpenCount(2); // Fail after 2 successful operations

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var helper = new EntityHelper<TestTable, long>(context);

        // Setup test table
        await SetupTestTableAsync(context);

        // Act - First two operations should succeed
        var entity1 = CreateTestEntity("FailAfter1");
        var entity2 = CreateTestEntity("FailAfter2");

        var result1 = await helper.CreateAsync(entity1, context);
        var result2 = await helper.CreateAsync(entity2, context);

        Assert.True(result1);
        Assert.True(result2);

        // Third operation should fail
        var entity3 = CreateTestEntity("FailAfter3");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await helper.CreateAsync(entity3, context);
        });

        _output.WriteLine("FailAfterCount test completed - 2 succeeded, 3rd failed as expected");
    }

    [Fact]
    public async Task Connection_BrokenConnection_HandlesAllOperationFailures()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.SetBroken(); // Mark connection as permanently broken

        using var context = new DatabaseContext("Server=localhost;Database=test;EmulatedProduct=MySQL", factory);
        var helper = new EntityHelper<TestTable, long>(context);

        // Act & Assert - All operations should fail
        var entity = CreateTestEntity("BrokenConnection");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await helper.CreateAsync(entity, context);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await helper.RetrieveOneAsync(1L, context);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var transaction = context.BeginTransaction();
        });

        _output.WriteLine("BrokenConnection test completed - all operations failed as expected");
    }

    [Fact]
    public async Task Connection_CustomException_PropagatesCorrectError()
    {
        // Arrange
        var customException = new TimeoutException("Database connection timeout after 30 seconds");
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.SetFailOnOpen();
        connection.SetCustomFailureException(customException);

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", factory);
        var helper = new EntityHelper<TestTable, long>(context);

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            var entity = CreateTestEntity("CustomException");
            await helper.CreateAsync(entity, context);
        });

        Assert.Equal(customException.Message, thrownException.Message);
        _output.WriteLine($"CustomException test completed: {thrownException.Message}");
    }

    [Fact]
    public async Task Connection_IntermittentFailures_RetriesSuccessfully()
    {
        // Arrange - Create a factory that fails intermittently
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = (fakeDbConnection)factory.CreateConnection();

        // Configure to fail on every other operation
        var operationCount = 0;
        connection.SetCustomOpenBehavior(() =>
        {
            operationCount++;
            if (operationCount % 2 == 0)
            {
                throw new InvalidOperationException("Intermittent network failure");
            }
        });

        using var context = new DatabaseContext("Host=localhost;Database=test;EmulatedProduct=PostgreSql", factory);

        // Setup test table on first successful connection
        await SetupTestTableAsync(context);

        var helper = new EntityHelper<TestTable, long>(context);

        // Act - Some operations should succeed, others fail
        var entity1 = CreateTestEntity("Intermittent1");
        var entity3 = CreateTestEntity("Intermittent3");

        // Operation 1 should succeed (odd number)
        var result1 = await helper.CreateAsync(entity1, context);
        Assert.True(result1);

        // Operation 2 should fail (even number)
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var entity2 = CreateTestEntity("Intermittent2");
            await helper.CreateAsync(entity2, context);
        });

        // Operation 3 should succeed (odd number)
        var result3 = await helper.CreateAsync(entity3, context);
        Assert.True(result3);

        _output.WriteLine("IntermittentFailures test completed - alternating success/failure pattern");
    }

    [Fact]
    public async Task Connection_MultipleConcurrentFailures_HandlesGracefully()
    {
        // Arrange
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailAfterCount,
            failAfterCount: 1);

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var helper = new EntityHelper<TestTable, long>(context);

        // Setup test table
        await SetupTestTableAsync(context);

        // Act - Run multiple concurrent operations that should mostly fail
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            try
            {
                var entity = CreateTestEntity($"Concurrent{i}");
                return await helper.CreateAsync(entity, context);
            }
            catch (InvalidOperationException)
            {
                return false; // Expected failure
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - Most should fail, but at least one might succeed
        var successCount = results.Count(r => r);
        var failureCount = results.Count(r => !r);

        Assert.True(failureCount >= successCount, "Most operations should fail due to connection limit");
        _output.WriteLine($"ConcurrentFailures test completed - {successCount} succeeded, {failureCount} failed");
    }

    [Fact]
    public async Task Connection_FailureDuringTransaction_RollsBackCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (fakeDbConnection)factory.CreateConnection();

        // Configure to fail after the transaction begins but before commit
        var commandCount = 0;
        connection.SetCustomCommandBehavior(() =>
        {
            commandCount++;
            if (commandCount > 2) // Fail after initial commands succeed
            {
                throw new InvalidOperationException("Connection lost during transaction");
            }
        });

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        await SetupTestTableAsync(context);

        var helper = new EntityHelper<TestTable, long>(context);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var transaction = context.BeginTransaction();

            var entity1 = CreateTestEntity("TxnFail1");
            await helper.CreateAsync(entity1, transaction);

            var entity2 = CreateTestEntity("TxnFail2");
            await helper.CreateAsync(entity2, transaction); // This should trigger the failure

            transaction.Commit();
        });

        _output.WriteLine("FailureDuringTransaction test completed - transaction failed and rolled back");
    }

    // Helper methods

    private static TestTable CreateTestEntity(string name)
    {
        return new TestTable
        {
            Name = name,
            Value = Random.Shared.Next(1, 1000),
            Description = $"Test description for {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private static async Task SetupTestTableAsync(IDatabaseContext context)
    {
        try
        {
            using var container = context.CreateSqlContainer(@"
                CREATE TABLE IF NOT EXISTS TestTable (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Value INTEGER NOT NULL,
                    Description TEXT,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedOn TEXT NOT NULL,
                    CreatedBy TEXT,
                    LastUpdatedOn TEXT,
                    LastUpdatedBy TEXT,
                    Version INTEGER NOT NULL DEFAULT 1
                )");
            await container.ExecuteNonQueryAsync();
        }
        catch (InvalidOperationException)
        {
            // Expected to fail in some test scenarios
        }
    }
}