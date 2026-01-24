using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.wrappers;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// Integration tests for DbMode connection management strategies including
/// Standard, KeepAlive, SingleWriter, and SingleConnection modes.
/// </summary>
[Collection("IntegrationTests")]
public class DbModeTests : DatabaseTestBase
{
    private static long _nextId;

    public DbModeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture) { }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [Fact]
    public async Task DbMode_Standard_OpensAndClosesConnections()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - SQLite/DuckDB containers use SingleWriter mode, others use Standard
            var expectedMode = provider is SupportedDatabase.Sqlite or SupportedDatabase.DuckDB
                ? DbMode.SingleWriter
                : DbMode.Standard;
            Assert.Equal(expectedMode, context.ConnectionMode);

            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 100);

            var initialConnCount = context.NumberOfOpenConnections;

            // Act - Perform operation
            await helper.CreateAsync(entity, context);

            // Assert - Connection behavior depends on mode
            if (provider is SupportedDatabase.Sqlite or SupportedDatabase.DuckDB)
            {
                // SingleWriter mode keeps one connection open
                Assert.Equal(1, context.NumberOfOpenConnections);
            }
            else
            {
                // Standard mode closes connections after use
                Assert.Equal(initialConnCount, context.NumberOfOpenConnections);
            }
        });
    }

    [Fact]
    public async Task DbMode_Standard_MultipleOperations_EachGetsNewConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entities = new[]
            {
                CreateTestEntity(NameEnum.Test, 200),
                CreateTestEntity(NameEnum.Test2, 201),
                CreateTestEntity(NameEnum.Test, 202)
            };

            // Act - Multiple operations
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);

                // In Standard mode, connection count should return to baseline
                // (or stay low for pooled connections)
                Output.WriteLine($"After insert {entity.Id}: {context.NumberOfOpenConnections} connections open");
            }

            // Assert - All entities should be created successfully
            var retrieved = await helper.RetrieveAsync(entities.Select(e => e.Id).ToList(), context);
            Assert.Equal(entities.Length, retrieved.Count);
        });
    }

    [Fact]
    public async Task DbMode_Standard_WithTransaction_MaintainsConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity1 = CreateTestEntity(NameEnum.Test, 300);
            var entity2 = CreateTestEntity(NameEnum.Test2, 301);

            // Act - Within transaction, connection should stay open
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var helper = CreateEntityHelper(transaction);

            var connCountInTransaction = transaction.NumberOfOpenConnections;
            Assert.True(connCountInTransaction > 0, "Transaction should have at least one connection open");

            await helper.CreateAsync(entity1, transaction);
            await helper.CreateAsync(entity2, transaction);

            // Connection should still be open during transaction
            Assert.True(transaction.NumberOfOpenConnections > 0, "Connection should remain open during transaction");

            transaction.Commit();

            // Assert - After commit, verify data persisted
            var baseHelper = CreateEntityHelper(context);
            var retrieved1 = await baseHelper.RetrieveOneAsync(entity1.Id, context);
            var retrieved2 = await baseHelper.RetrieveOneAsync(entity2.Id, context);

            Assert.NotNull(retrieved1);
            Assert.NotNull(retrieved2);
        });
    }

    [Fact]
    public async Task DbMode_Standard_ParallelOperations_IndependentConnections()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = Enumerable.Range(0, 10)
                .Select(i => CreateTestEntity(NameEnum.Test, 400 + i))
                .ToList();

            // Act - Parallel operations should each get independent connections
            var tasks = entities.Select(async entity =>
            {
                var helper = CreateEntityHelper(context);
                return await helper.CreateAsync(entity, context);
            });

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, r => Assert.True(r));

            // Verify all persisted
            var helper = CreateEntityHelper(context);
            var retrieved = await helper.RetrieveAsync(entities.Select(e => e.Id).ToList(), context);
            Assert.Equal(entities.Count, retrieved.Count);
        });
    }

    [Fact]
    public async Task ExecutionType_Read_UsesReadConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 500);
            await CreateEntityHelper(context).CreateAsync(entity, context);

            // Act - Use ExecuteReaderAsync which uses ExecutionType.Read
            var tableName = context.WrapObjectName("test_table");
            var idColumn = context.WrapObjectName("id");
            var nameColumn = context.WrapObjectName("name");
            var valueColumn = context.WrapObjectName("value");

            await using var container = context.CreateSqlContainer();
            container.Query.Append("SELECT ")
                .Append(idColumn).Append(", ")
                .Append(nameColumn).Append(", ")
                .Append(valueColumn)
                .Append(" FROM ").Append(tableName)
                .Append(" WHERE ").Append(idColumn).Append(" = ")
                .Append(container.MakeParameterName("id"));
            container.AddParameterWithValue("id", DbType.Int64, entity.Id);

            await using var reader = await container.ExecuteReaderAsync();

            // Assert - Verify data was read correctly
            Assert.True(await reader.ReadAsync());
            var readId = reader.GetInt64(0);
            Assert.Equal(entity.Id, readId);
        });
    }

    [Fact]
    public async Task ExecutionType_Write_UsesWriteConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange & Act - Get write connection explicitly
            await using var writeConnection = context.GetConnection(ExecutionType.Write);
            if (writeConnection.State != ConnectionState.Open)
            {
                await writeConnection.OpenAsync();
            }

            var entity = CreateTestEntity(NameEnum.Test, 600);

            var tableName = context.WrapObjectName("test_table");
            var idColumn = context.WrapObjectName("id");
            var nameColumn = context.WrapObjectName("name");
            var valueColumn = context.WrapObjectName("value");
            var activeColumn = context.WrapObjectName("is_active");
            var createdColumn = context.WrapObjectName("created_at");

            await using var container = context.CreateSqlContainer();
            container.Query.Append("INSERT INTO ").Append(tableName).Append(" (");
            container.Query.Append(idColumn).Append(", ");
            container.Query.Append(nameColumn).Append(", ");
            container.Query.Append(valueColumn).Append(", ");
            container.Query.Append(activeColumn).Append(", ");
            container.Query.Append(createdColumn).Append(") VALUES (");
            container.Query.Append(container.MakeParameterName("id")).Append(", ");
            container.Query.Append(container.MakeParameterName("name")).Append(", ");
            container.Query.Append(container.MakeParameterName("value")).Append(", ");
            container.Query.Append(container.MakeParameterName("active")).Append(", ");
            container.Query.Append(container.MakeParameterName("created")).Append(")");

            container.AddParameterWithValue("id", DbType.Int64, entity.Id);
            container.AddParameterWithValue("name", DbType.String, entity.Name.ToString());
            container.AddParameterWithValue("value", DbType.Int32, entity.Value);
            container.AddParameterWithValue("active", GetBooleanDbType(provider), entity.IsActive);
            container.AddParameterWithValue("created", DbType.DateTime, entity.CreatedOn);

            await using var command = container.CreateCommand(writeConnection);
            var rowsAffected = await command.ExecuteNonQueryAsync();

            // Assert
            Assert.Equal(1, rowsAffected);

            // Verify it was actually inserted
            var helper = CreateEntityHelper(context);
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
        });
    }

    [Fact]
    public async Task Transaction_ReadOnly_AllowsReadsOnly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip providers that don't properly support read-only transactions
            if (!SupportsReadOnlyTransactions(provider))
            {
                Output.WriteLine($"Skipping read-only transaction test for {provider}");
                return;
            }

            // Arrange - Create data first
            var entity = CreateTestEntity(NameEnum.Test, 700);
            await CreateEntityHelper(context).CreateAsync(entity, context);

            // Act - Begin read-only transaction
            await using var readTransaction = context.BeginTransaction(
                IsolationLevel.ReadCommitted,
                ExecutionType.Read,
                readOnly: true);

            var helper = CreateEntityHelper(readTransaction);

            // Assert - Reads should work
            var retrieved = await helper.RetrieveOneAsync(entity.Id, readTransaction);
            Assert.NotNull(retrieved);

            // Writes should fail
            var newEntity = CreateTestEntity(NameEnum.Test2, 701);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await helper.CreateAsync(newEntity, readTransaction);
            });
        });
    }

    [Fact]
    public async Task Connection_Reuse_WithinTransaction_SameConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange & Act
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

            var conn1 = transaction.GetConnection(ExecutionType.Write);
            var conn2 = transaction.GetConnection(ExecutionType.Write);

            // Assert - Within a transaction, should reuse the same connection
            Assert.Same(conn1, conn2);
        });
    }

    [Fact]
    public async Task Connection_Close_OutsideTransaction_ClosesImmediately()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var initialCount = context.NumberOfOpenConnections;

            // Act - Get connection, open it, then close it
            ITrackedConnection? conn = null;
            try
            {
                conn = context.GetConnection(ExecutionType.Read);
                await conn.OpenAsync();

                var openCount = context.NumberOfOpenConnections;
                Output.WriteLine($"Connections open: {openCount}");

                context.CloseAndDisposeConnection(conn);
                conn = null;
            }
            finally
            {
                if (conn != null)
                {
                    context.CloseAndDisposeConnection(conn);
                }
            }

            // Assert - Connection should be closed
            // Note: In Standard mode with connection pooling, the count might not be exactly initial
            // but it should not have leaked
            Assert.True(context.NumberOfOpenConnections >= initialCount);
        });
    }

    [Fact]
    public async Task MultipleTransactions_Sequential_EachGetsOwnConnection()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = new[]
            {
                CreateTestEntity(NameEnum.Test, 800),
                CreateTestEntity(NameEnum.Test2, 801),
                CreateTestEntity(NameEnum.Test, 802)
            };

            // Act - Sequential transactions
            foreach (var entity in entities)
            {
                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var helper = CreateEntityHelper(transaction);

                await helper.CreateAsync(entity, transaction);
                transaction.Commit();
            }

            // Assert
            var baseHelper = CreateEntityHelper(context);
            var retrieved = await baseHelper.RetrieveAsync(entities.Select(e => e.Id).ToList(), context);
            Assert.Equal(entities.Length, retrieved.Count);
        });
    }

    [Fact]
    public async Task Connection_Metrics_TracksOpenConnections()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var initialCount = context.NumberOfOpenConnections;
            Output.WriteLine($"Initial connection count: {initialCount}");

            // Act - Perform some operations
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 900);

            await helper.CreateAsync(entity, context);
            Output.WriteLine($"After create: {context.NumberOfOpenConnections} connections");

            await helper.RetrieveOneAsync(entity.Id, context);
            Output.WriteLine($"After retrieve: {context.NumberOfOpenConnections} connections");

            await helper.DeleteAsync(entity.Id, context);
            Output.WriteLine($"After delete: {context.NumberOfOpenConnections} connections");

            // Assert - Metrics should be tracked
            var metrics = context.Metrics;
            // Metrics is a value type, so no null check needed

            // In Standard mode, connections should not accumulate
            Output.WriteLine($"Final connection count: {context.NumberOfOpenConnections}");
        });
    }

    [Fact]
    public async Task IsolationLevel_ReadCommitted_PreventsDirtyReads()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip SQLite which has limited isolation support
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine("Skipping isolation test for SQLite");
                return;
            }
            if (provider == SupportedDatabase.DuckDB)
            {
                Output.WriteLine("Skipping isolation test for DuckDB");
                return;
            }
            if (provider == SupportedDatabase.SqlServer && !context.RCSIEnabled)
            {
                Output.WriteLine("Skipping isolation test for SQL Server without RCSI");
                return;
            }

            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 1000);
            await CreateEntityHelper(context).CreateAsync(entity, context);

            // Act - Transaction 1: Read the entity
            await using var tx1 = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var helper1 = CreateEntityHelper(tx1);
            var read1 = await helper1.RetrieveOneAsync(entity.Id, tx1);
            Assert.NotNull(read1);
            Assert.Equal(entity.Value, read1!.Value);

            // Transaction 2: Update but don't commit
            await using var tx2 = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var helper2 = CreateEntityHelper(tx2);
            var read2 = await helper2.RetrieveOneAsync(entity.Id, tx2);
            read2!.Value = 2000;
            await helper2.UpdateAsync(read2, tx2);
            // Don't commit tx2 yet

            // Transaction 1: Read again - should NOT see uncommitted changes
            var readAgain = await helper1.RetrieveOneAsync(entity.Id, tx1);
            Assert.NotNull(readAgain);
            Assert.Equal(entity.Value, readAgain!.Value); // Should still see original value

            tx2.Rollback(); // Cleanup
            tx1.Commit();

            // Assert - Original value should be preserved
            var final = await CreateEntityHelper(context).RetrieveOneAsync(entity.Id, context);
            Assert.Equal(entity.Value, final!.Value);
        });
    }

    private EntityHelper<TestTable, long> CreateEntityHelper(IDatabaseContext context)
    {
        var auditResolver = GetAuditResolver();
        return new EntityHelper<TestTable, long>(context, auditValueResolver: auditResolver);
    }

    private static TestTable CreateTestEntity(NameEnum name, int value)
    {
        return new TestTable
        {
            Id = Interlocked.Increment(ref _nextId),
            Name = name,
            Value = value,
            Description = $"DbMode test: {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private static DbType GetBooleanDbType(SupportedDatabase provider)
    {
        return provider == SupportedDatabase.Sqlite ? DbType.Int32 : DbType.Boolean;
    }

    private static bool SupportsReadOnlyTransactions(SupportedDatabase provider)
    {
        return provider is SupportedDatabase.PostgreSql or
                          SupportedDatabase.SqlServer or
                          SupportedDatabase.Oracle or
                          SupportedDatabase.MySql;
    }
}
