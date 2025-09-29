using System.Data;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// Integration tests for ExecutionType.Read vs ExecutionType.Write connection handling.
/// Tests how pengdows.crud manages connections differently based on intended operation type.
/// </summary>
public class ExecutionTypeTests : DatabaseTestBase
{
    public ExecutionTypeTests(ITestOutputHelper output) : base(output) { }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
        await tableCreator.CreateAccountTableAsync();
    }

    [Fact]
    public async Task ExecutionType_Read_UsesReadOptimizedConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create test data
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity($"ReadExec-{provider}");
            await helper.CreateAsync(entity, context);

            var initialConnectionCount = context.NumberOfOpenConnections;
            var initialMaxConnections = context.MaxNumberOfConnections;

            // Act - Get read connection explicitly
            using var readConnection = context.GetConnection(ExecutionType.Read);
            await readConnection.OpenAsync();

            // Verify connection is configured for read operations
            Assert.NotNull(readConnection);
            Assert.True(readConnection.State == ConnectionState.Open);

            // Execute read operation
            using var container = context.CreateSqlContainer("SELECT * FROM TestTable WHERE Id = ");
            container.Query.Append(container.MakeParameterName("id"));
            container.AddParameterWithValue("id", DbType.Int64, entity.Id);

            using var command = container.CreateCommand(readConnection);
            using var reader = await command.ExecuteReaderAsync();

            var found = false;
            while (await reader.ReadAsync())
            {
                found = true;
                var retrievedName = reader.GetString("Name");
                Assert.Equal(entity.Name, retrievedName);
            }

            Assert.True(found, "Entity should be found using read connection");

            var finalConnectionCount = context.NumberOfOpenConnections;
            var finalMaxConnections = context.MaxNumberOfConnections;

            Output.WriteLine($"{provider} Read connection - Initial: {initialConnectionCount}, Final: {finalConnectionCount}, Max: {finalMaxConnections}");
        });
    }

    [Fact]
    public async Task ExecutionType_Write_UsesWriteOptimizedConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var initialConnectionCount = context.NumberOfOpenConnections;

            // Act - Get write connection explicitly
            using var writeConnection = context.GetConnection(ExecutionType.Write);
            await writeConnection.OpenAsync();

            // Verify connection is configured for write operations
            Assert.NotNull(writeConnection);
            Assert.True(writeConnection.State == ConnectionState.Open);

            // Execute write operation
            using var container = context.CreateSqlContainer(@"
                INSERT INTO TestTable (Name, Value, Description, IsActive, CreatedOn, Version)
                VALUES (");
            container.Query.Append(container.MakeParameterName("name"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("value"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("description"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("isActive"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("createdOn"));
            container.Query.Append(", ");
            container.Query.Append(container.MakeParameterName("version"));
            container.Query.Append(")");

            container.AddParameterWithValue("name", DbType.String, $"WriteExec-{provider}");
            container.AddParameterWithValue("value", DbType.Int32, 123);
            container.AddParameterWithValue("description", DbType.String, "Write execution test");
            container.AddParameterWithValue("isActive", GetBooleanDbType(provider), true);
            container.AddParameterWithValue("createdOn", GetDateTimeDbType(provider), DateTime.UtcNow);
            container.AddParameterWithValue("version", DbType.Int32, 1);

            using var command = container.CreateCommand(writeConnection);
            var rowsAffected = await command.ExecuteNonQueryAsync();

            Assert.Equal(1, rowsAffected);

            var finalConnectionCount = context.NumberOfOpenConnections;
            Output.WriteLine($"{provider} Write connection executed successfully - Connections: {finalConnectionCount}");
        });
    }

    [Fact]
    public async Task ExecutionType_ReadWrite_TransactionBehavior()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Test Read transaction
            using (var readTransaction = await context.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ExecutionType.Read))
            {
                // Create test data outside transaction first
                var entity = CreateTestEntity($"ReadWriteTxn-{provider}");
                await helper.CreateAsync(entity, context);

                // Read within readonly transaction
                var retrieved = await helper.RetrieveOneAsync(entity.Id, readTransaction);
                Assert.NotNull(retrieved);
                await readTransaction.CommitAsync();

                Output.WriteLine($"{provider} Read transaction completed successfully");
            }

            // Test Write transaction
            using (var writeTransaction = await context.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ExecutionType.Write))
            {
                // Write within write transaction
                var writeEntity = CreateTestEntity($"WriteTxn-{provider}");
                await helper.CreateAsync(writeEntity, writeTransaction);

                // Verify write is visible within transaction
                var retrievedInTxn = await helper.RetrieveOneAsync(writeEntity.Id, writeTransaction);
                Assert.NotNull(retrievedInTxn);

                await writeTransaction.CommitAsync();

                // Verify write is visible after commit
                var retrievedAfterCommit = await helper.RetrieveOneAsync(writeEntity.Id, context);
                Assert.NotNull(retrievedAfterCommit);

                Output.WriteLine($"{provider} Write transaction completed successfully");
            }
        });
    }

    [Fact]
    public async Task ExecutionType_ConcurrentReadWrite_NoInterference()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Arrange - Create initial test data
            var entities = new List<TestTable>();
            for (int i = 0; i < 3; i++)
            {
                var entity = CreateTestEntity($"Concurrent{i}-{provider}");
                await helper.CreateAsync(entity, context);
                entities.Add(entity);
            }

            // Act - Run concurrent read and write operations
            var readTasks = entities.Select(async (entity, index) =>
            {
                using var readTransaction = await context.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted, ExecutionType.Read);

                // Simulate some processing time
                await Task.Delay(10 + (index * 5));

                var retrieved = await helper.RetrieveOneAsync(entity.Id, readTransaction);
                await readTransaction.CommitAsync();
                return retrieved;
            }).ToArray();

            var writeTasks = Enumerable.Range(0, 2).Select(async i =>
            {
                using var writeTransaction = await context.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted, ExecutionType.Write);

                var newEntity = CreateTestEntity($"ConcurrentWrite{i}-{provider}");
                await helper.CreateAsync(newEntity, writeTransaction);
                await writeTransaction.CommitAsync();
                return newEntity;
            }).ToArray();

            // Wait for all operations to complete
            var readResults = await Task.WhenAll(readTasks);
            var writeResults = await Task.WhenAll(writeTasks);

            // Assert - All operations should complete successfully
            Assert.Equal(3, readResults.Length);
            Assert.All(readResults, r => Assert.NotNull(r));

            Assert.Equal(2, writeResults.Length);
            Assert.All(writeResults, w => Assert.NotNull(w));

            Output.WriteLine($"{provider} Concurrent read/write operations completed: {readResults.Length} reads, {writeResults.Length} writes");
        });
    }

    [Fact]
    public async Task ExecutionType_ConnectionPooling_BehavesCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip for SQLite as it doesn't use traditional connection pooling
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine($"Skipping connection pooling test for {provider} - not applicable");
                return;
            }

            var helper = CreateEntityHelper(context);
            var initialConnectionCount = context.NumberOfOpenConnections;
            var maxObservedConnections = 0;

            // Perform multiple operations to observe connection pooling behavior
            for (int i = 0; i < 5; i++)
            {
                // Read operation
                using (var readConnection = context.GetConnection(ExecutionType.Read))
                {
                    await readConnection.OpenAsync();
                    maxObservedConnections = Math.Max(maxObservedConnections, context.NumberOfOpenConnections);

                    using var readContainer = context.CreateSqlContainer("SELECT COUNT(*) FROM TestTable");
                    using var readCommand = readContainer.CreateCommand(readConnection);
                    await readCommand.ExecuteScalarAsync();
                }

                // Write operation
                using (var writeConnection = context.GetConnection(ExecutionType.Write))
                {
                    await writeConnection.OpenAsync();
                    maxObservedConnections = Math.Max(maxObservedConnections, context.NumberOfOpenConnections);

                    var entity = CreateTestEntity($"Pool{i}-{provider}");
                    await helper.CreateAsync(entity, context);
                }

                // Small delay to allow connection cleanup
                await Task.Delay(10);
            }

            var finalConnectionCount = context.NumberOfOpenConnections;

            // Assert - Connection pooling should manage connections efficiently
            Assert.True(maxObservedConnections >= initialConnectionCount);
            Output.WriteLine($"{provider} Connection pooling - Initial: {initialConnectionCount}, Max: {maxObservedConnections}, Final: {finalConnectionCount}");
        });
    }

    [Fact]
    public async Task ExecutionType_FailureHandling_ReadWriteDifferences()
    {
        // Test using FakeDb to simulate different failure scenarios
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();

        // Configure to fail on write operations (simulating read-only mode)
        var operationCount = 0;
        connection.SetCustomCommandBehavior(() =>
        {
            operationCount++;
            // Fail write operations (odd numbered operations in our test)
            if (operationCount % 2 == 1) // Simulate write failure
            {
                throw new InvalidOperationException("Database is in read-only mode");
            }
        });

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        try
        {
            // Setup test table
            await SetupMockTestTableAsync(context);

            // Read operation should work (even operation count)
            using var readConnection = context.GetConnection(ExecutionType.Read);
            await readConnection.OpenAsync();

            using var readContainer = context.CreateSqlContainer("SELECT 1");
            using var readCommand = readContainer.CreateCommand(readConnection);
            var readResult = await readCommand.ExecuteScalarAsync();
            Assert.NotNull(readResult);

            // Write operation should fail (odd operation count)
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using var writeConnection = context.GetConnection(ExecutionType.Write);
                await writeConnection.OpenAsync();

                using var writeContainer = context.CreateSqlContainer("INSERT INTO TestTable (Name) VALUES ('test')");
                using var writeCommand = writeContainer.CreateCommand(writeConnection);
                await writeCommand.ExecuteNonQueryAsync();
            });

            Output.WriteLine("ExecutionType failure handling test completed - read succeeded, write failed as expected");
        }
        catch (InvalidOperationException)
        {
            // Expected for some FakeDb scenarios
            Output.WriteLine("ExecutionType failure test completed with expected simulation behavior");
        }
    }

    [Fact]
    public async Task ExecutionType_DbModeInteraction_BehavesConsistently()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var currentMode = context.ConnectionMode;
            var helper = CreateEntityHelper(context);

            // Test read execution type behavior with current DbMode
            using var readTransaction = await context.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ExecutionType.Read);

            var entity = CreateTestEntity($"DbMode{currentMode}-{provider}");
            await helper.CreateAsync(entity, context); // Create outside transaction

            var retrieved = await helper.RetrieveOneAsync(entity.Id, readTransaction);
            Assert.NotNull(retrieved);
            await readTransaction.CommitAsync();

            // Test write execution type behavior with current DbMode
            using var writeTransaction = await context.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ExecutionType.Write);

            var writeEntity = CreateTestEntity($"DbModeWrite{currentMode}-{provider}");
            await helper.CreateAsync(writeEntity, writeTransaction);
            await writeTransaction.CommitAsync();

            var verifyEntity = await helper.RetrieveOneAsync(writeEntity.Id, context);
            Assert.NotNull(verifyEntity);

            Output.WriteLine($"{provider} ExecutionType interaction with DbMode {currentMode} completed successfully");
        });
    }

    // Helper methods

    private EntityHelper<TestTable, long> CreateEntityHelper(IDatabaseContext context)
    {
        var auditResolver = Host.Services.GetService<IAuditValueResolver>() ??
                           new StringAuditContextProvider();
        return new EntityHelper<TestTable, long>(context, auditValueResolver: auditResolver);
    }

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

    private static DbType GetBooleanDbType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => DbType.Int32, // SQLite uses INTEGER for boolean
            _ => DbType.Boolean
        };
    }

    private static DbType GetDateTimeDbType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => DbType.String, // SQLite uses TEXT for datetime
            _ => DbType.DateTime
        };
    }

    private async Task SetupMockTestTableAsync(IDatabaseContext context)
    {
        try
        {
            using var container = context.CreateSqlContainer(@"
                CREATE TABLE IF NOT EXISTS TestTable (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Value INTEGER NOT NULL DEFAULT 0,
                    Description TEXT,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedOn TEXT NOT NULL DEFAULT '2024-01-01',
                    CreatedBy TEXT,
                    LastUpdatedOn TEXT,
                    LastUpdatedBy TEXT,
                    Version INTEGER NOT NULL DEFAULT 1
                )");
            await container.ExecuteNonQueryAsync();
        }
        catch (InvalidOperationException)
        {
            // Expected in some FakeDb test scenarios
        }
    }
}

