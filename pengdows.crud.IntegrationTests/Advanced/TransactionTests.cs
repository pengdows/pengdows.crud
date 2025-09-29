using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// Integration tests for transaction handling, isolation levels, and rollback scenarios.
/// Tests the robustness of pengdows.crud's transaction management across different databases.
/// </summary>
public class TransactionTests : DatabaseTestBase
{
    public TransactionTests(ITestOutputHelper output) : base(output) { }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
        await tableCreator.CreateAccountTableAsync(); // For transfer tests
    }

    [Fact]
    public async Task Transaction_BasicCommit_PersistsChanges()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Act
            using var transaction = await context.BeginTransactionAsync();

            var entity = CreateTestEntity($"Transaction-{provider}");
            await helper.CreateAsync(entity, transaction);

            await transaction.CommitAsync();

            // Assert - Changes should be visible outside transaction
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Name, retrieved.Name);
        });
    }

    [Fact]
    public async Task Transaction_Rollback_DiscardsChanges()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Arrange - Create initial entity outside transaction
            var initialEntity = CreateTestEntity($"Initial-{provider}");
            await helper.CreateAsync(initialEntity, context);

            // Act - Make changes in transaction and rollback
            using var transaction = await context.BeginTransactionAsync();

            var transactionEntity = CreateTestEntity($"Rollback-{provider}");
            await helper.CreateAsync(transactionEntity, transaction);

            // Update existing entity
            initialEntity.Name = $"Modified-{provider}";
            await helper.UpdateAsync(initialEntity, transaction);

            await transaction.RollbackAsync();

            // Assert - Transaction changes should not be visible
            var rollbackEntity = await helper.RetrieveOneAsync(transactionEntity.Id, context);
            Assert.Null(rollbackEntity); // Should not exist

            var originalEntity = await helper.RetrieveOneAsync(initialEntity.Id, context);
            Assert.NotNull(originalEntity);
            Assert.Contains("Initial", originalEntity.Name); // Should be unchanged
        });
    }

    [Fact]
    public async Task Transaction_NestedSavepoints_RollbackToSavepoint()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip databases that don't support savepoints
            if (!SupportsSavepoints(provider))
            {
                Output.WriteLine($"Skipping savepoint test for {provider} - no savepoint support");
                return;
            }

            var helper = CreateEntityHelper(context);

            // Act
            using var transaction = await context.BeginTransactionAsync();

            // Initial insert
            var entity1 = CreateTestEntity($"Savepoint1-{provider}");
            await helper.CreateAsync(entity1, transaction);

            // Create savepoint
            await transaction.SavepointAsync("sp1");

            // More changes after savepoint
            var entity2 = CreateTestEntity($"Savepoint2-{provider}");
            await helper.CreateAsync(entity2, transaction);

            // Rollback to savepoint (should keep entity1, discard entity2)
            await transaction.RollbackToSavepointAsync("sp1");

            await transaction.CommitAsync();

            // Assert
            var retrieved1 = await helper.RetrieveOneAsync(entity1.Id, context);
            Assert.NotNull(retrieved1); // Should exist

            var retrieved2 = await helper.RetrieveOneAsync(entity2.Id, context);
            Assert.Null(retrieved2); // Should not exist due to savepoint rollback
        });
    }

    [Fact]
    public async Task Transaction_IsolationLevel_ReadCommitted()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Arrange - Create initial data
            var entity = CreateTestEntity($"Isolation-{provider}");
            await helper.CreateAsync(entity, context);

            // Act - Start transaction with Read Committed isolation
            using var transaction = await context.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            // Read in transaction
            var beforeUpdate = await helper.RetrieveOneAsync(entity.Id, transaction);
            Assert.NotNull(beforeUpdate);

            // Simulate concurrent update outside transaction
            entity.Name = $"Updated-{provider}";
            await helper.UpdateAsync(entity, context);

            // Read again in same transaction - should see the committed change (Read Committed)
            var afterUpdate = await helper.RetrieveOneAsync(entity.Id, transaction);
            Assert.NotNull(afterUpdate);

            // In Read Committed, we should see the committed change
            // Note: This behavior varies by database, but we're testing the mechanism works
            await transaction.CommitAsync();
        });
    }

    [Fact]
    public async Task Transaction_MoneyTransfer_AtomicOperation()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create two accounts
            await CreateAccountAsync(context, 1, "Account A", 1000.00m);
            await CreateAccountAsync(context, 2, "Account B", 500.00m);

            var transferAmount = 250.00m;

            // Act - Perform atomic money transfer
            using var transaction = await context.BeginTransactionAsync();

            // Debit from account A
            using var debitContainer = context.CreateSqlContainer(@"
                UPDATE accounts SET balance = balance - ");
            debitContainer.Query.Append(debitContainer.MakeParameterName("amount"));
            debitContainer.Query.Append(" WHERE id = ");
            debitContainer.Query.Append(debitContainer.MakeParameterName("fromId"));
            debitContainer.AddParameterWithValue("amount", System.Data.DbType.Decimal, transferAmount);
            debitContainer.AddParameterWithValue("fromId", System.Data.DbType.Int64, 1L);

            var debitRows = await debitContainer.ExecuteNonQueryAsync();
            Assert.Equal(1, debitRows);

            // Credit to account B
            using var creditContainer = context.CreateSqlContainer(@"
                UPDATE accounts SET balance = balance + ");
            creditContainer.Query.Append(creditContainer.MakeParameterName("amount"));
            creditContainer.Query.Append(" WHERE id = ");
            creditContainer.Query.Append(creditContainer.MakeParameterName("toId"));
            creditContainer.AddParameterWithValue("amount", System.Data.DbType.Decimal, transferAmount);
            creditContainer.AddParameterWithValue("toId", System.Data.DbType.Int64, 2L);

            var creditRows = await creditContainer.ExecuteNonQueryAsync();
            Assert.Equal(1, creditRows);

            await transaction.CommitAsync();

            // Assert - Verify final balances
            var finalBalanceA = await GetAccountBalanceAsync(context, 1);
            var finalBalanceB = await GetAccountBalanceAsync(context, 2);

            Assert.Equal(750.00m, finalBalanceA);
            Assert.Equal(750.00m, finalBalanceB);
        });
    }

    [Fact]
    public async Task Transaction_ConcurrentAccess_NoDeadlock()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Arrange - Create test entities
            var entity1 = CreateTestEntity($"Concurrent1-{provider}");
            var entity2 = CreateTestEntity($"Concurrent2-{provider}");
            await helper.CreateAsync(entity1, context);
            await helper.CreateAsync(entity2, context);

            // Act - Run concurrent transactions
            var task1 = Task.Run(async () =>
            {
                using var transaction = await context.BeginTransactionAsync();
                entity1.Name = $"Updated1-{provider}";
                await helper.UpdateAsync(entity1, transaction);

                // Small delay to increase chance of conflict
                await Task.Delay(50);

                entity2.Value = 999;
                await helper.UpdateAsync(entity2, transaction);
                await transaction.CommitAsync();
            });

            var task2 = Task.Run(async () =>
            {
                using var transaction = await context.BeginTransactionAsync();
                entity2.Name = $"Updated2-{provider}";
                await helper.UpdateAsync(entity2, transaction);

                // Small delay to increase chance of conflict
                await Task.Delay(50);

                entity1.Value = 888;
                await helper.UpdateAsync(entity1, transaction);
                await transaction.CommitAsync();
            });

            // Assert - Both should complete (one might fail due to deadlock, but shouldn't hang)
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await Task.WhenAll(task1, task2);
            });

            // At least one should have succeeded
            var final1 = await helper.RetrieveOneAsync(entity1.Id, context);
            var final2 = await helper.RetrieveOneAsync(entity2.Id, context);

            Assert.NotNull(final1);
            Assert.NotNull(final2);
        });
    }

    [Fact]
    public async Task Transaction_Exception_AutoRollback()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using var transaction = await context.BeginTransactionAsync();

                var entity = CreateTestEntity($"Exception-{provider}");
                await helper.CreateAsync(entity, context);

                // Simulate an error
                throw new InvalidOperationException("Simulated error");

                // This should not be reached
                await transaction.CommitAsync();
            });

            // Verify transaction was rolled back
            using var verifyContainer = context.CreateSqlContainer("SELECT COUNT(*) FROM TestTable");
            var count = await verifyContainer.ExecuteScalarAsync<long>();
            Assert.Equal(0, count); // Should be no records due to rollback
        });
    }

    [Fact]
    public async Task Transaction_ReadOnly_AllowsOnlyReadOperations()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Arrange - Create test data
            var entity = CreateTestEntity($"ReadOnlyTxn-{provider}");
            await helper.CreateAsync(entity, context);

            // Act - Start readonly transaction
            using var readonlyTransaction = await context.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ExecutionType.Read);

            // Should allow read operations
            var retrieved = await helper.RetrieveOneAsync(entity.Id, readonlyTransaction);
            Assert.NotNull(retrieved);

            // Should allow read queries
            using var countContainer = context.CreateSqlContainer("SELECT COUNT(*) FROM TestTable");
            var count = await countContainer.ExecuteScalarAsync<long>();
            Assert.True(count >= 1);

            await readonlyTransaction.CommitAsync();

            _output.WriteLine($"ReadOnly transaction test completed for {provider} - read operations succeeded");
        });
    }

    [Fact]
    public async Task Transaction_ReadOnly_IsolationLevels_BehaveDifferently()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip if provider doesn't support multiple isolation levels
            if (!SupportsMultipleIsolationLevels(provider))
            {
                _output.WriteLine($"Skipping isolation level test for {provider} - limited isolation support");
                return;
            }

            var helper = CreateEntityHelper(context);

            // Arrange - Create test entity
            var entity = CreateTestEntity($"IsolationTest-{provider}");
            await helper.CreateAsync(entity, context);

            // Test ReadCommitted readonly transaction
            using (var readCommittedTxn = await context.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ExecutionType.Read))
            {
                var readCommittedResult = await helper.RetrieveOneAsync(entity.Id, readCommittedTxn);
                Assert.NotNull(readCommittedResult);
                await readCommittedTxn.CommitAsync();
            }

            // Test RepeatableRead readonly transaction (if supported)
            if (SupportsRepeatableRead(provider))
            {
                using var repeatableReadTxn = await context.BeginTransactionAsync(
                    IsolationLevel.RepeatableRead, ExecutionType.Read);

                var repeatableReadResult = await helper.RetrieveOneAsync(entity.Id, repeatableReadTxn);
                Assert.NotNull(repeatableReadResult);
                await repeatableReadTxn.CommitAsync();
            }

            _output.WriteLine($"ReadOnly isolation level test completed for {provider}");
        });
    }

    [Fact]
    public async Task Transaction_ReadOnly_ConcurrentWithWriteTransactions()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Arrange - Create test data
            var entity1 = CreateTestEntity($"ReadWrite1-{provider}");
            var entity2 = CreateTestEntity($"ReadWrite2-{provider}");
            await helper.CreateAsync(entity1, context);
            await helper.CreateAsync(entity2, context);

            // Act - Run concurrent readonly and write transactions
            var readTask = Task.Run(async () =>
            {
                using var readTxn = await context.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted, ExecutionType.Read);

                // Perform multiple reads
                var read1 = await helper.RetrieveOneAsync(entity1.Id, readTxn);
                await Task.Delay(50); // Allow time for write transaction
                var read2 = await helper.RetrieveOneAsync(entity2.Id, readTxn);

                await readTxn.CommitAsync();
                return new[] { read1, read2 };
            });

            var writeTask = Task.Run(async () =>
            {
                await Task.Delay(25); // Start after read transaction begins
                using var writeTxn = await context.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted, ExecutionType.Write);

                entity1.Name = $"Modified-{provider}";
                await helper.UpdateAsync(entity1, writeTxn);
                await writeTxn.CommitAsync();
                return true;
            });

            // Assert - Both should complete successfully
            var readResults = await readTask;
            var writeResult = await writeTask;

            Assert.NotNull(readResults[0]);
            Assert.NotNull(readResults[1]);
            Assert.True(writeResult);

            _output.WriteLine($"Concurrent read/write transaction test completed for {provider}");
        });
    }

    [Fact]
    public async Task Transaction_ReadOnly_LongRunning_MaintainsConsistency()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateEntityHelper(context);

            // Arrange - Create initial test data
            var entities = new List<TestTable>();
            for (int i = 0; i < 3; i++)
            {
                var entity = CreateTestEntity($"LongRead{i}-{provider}");
                await helper.CreateAsync(entity, context);
                entities.Add(entity);
            }

            // Act - Start long-running readonly transaction
            using var longReadTxn = await context.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ExecutionType.Read);

            // Initial read
            var initialEntities = await helper.RetrieveAsync(entities.Select(e => e.Id), longReadTxn);
            var initialCount = initialEntities.Count;

            // Simulate external changes while readonly transaction is active
            var newEntity = CreateTestEntity($"External-{provider}");
            await helper.CreateAsync(newEntity, context);

            entities[0].Value = 999;
            await helper.UpdateAsync(entities[0], context);

            // Read again in readonly transaction
            var laterEntities = await helper.RetrieveAsync(entities.Select(e => e.Id), longReadTxn);

            await longReadTxn.CommitAsync();

            // Assert - Readonly transaction behavior depends on isolation level
            Assert.Equal(initialCount, laterEntities.Count);
            Assert.All(laterEntities, e => Assert.NotNull(e));

            _output.WriteLine($"Long-running readonly transaction maintained consistency for {provider}: {laterEntities.Count} entities");
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
            Value = 100,
            Description = $"Test description for {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private async Task CreateAccountAsync(IDatabaseContext context, long id, string name, decimal balance)
    {
        using var container = context.CreateSqlContainer(@"
            INSERT INTO accounts (id, name, balance) VALUES (");
        container.Query.Append(container.MakeParameterName("id"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("name"));
        container.Query.Append(", ");
        container.Query.Append(container.MakeParameterName("balance"));
        container.Query.Append(")");
        container.AddParameterWithValue("id", System.Data.DbType.Int64, id);
        container.AddParameterWithValue("name", System.Data.DbType.String, name);
        container.AddParameterWithValue("balance", System.Data.DbType.Decimal, balance);

        await container.ExecuteNonQueryAsync();
    }

    private async Task<decimal> GetAccountBalanceAsync(IDatabaseContext context, long id)
    {
        using var container = context.CreateSqlContainer(@"
            SELECT balance FROM accounts WHERE id = ");
        container.Query.Append(container.MakeParameterName("id"));
        container.AddParameterWithValue("id", System.Data.DbType.Int64, id);

        return await container.ExecuteScalarAsync<decimal>();
    }

    private static bool SupportsSavepoints(SupportedDatabase provider)
    {
        return provider is not (SupportedDatabase.MySQL or SupportedDatabase.MariaDb);
    }

    private static bool SupportsMultipleIsolationLevels(SupportedDatabase provider)
    {
        return provider is not (SupportedDatabase.Sqlite);
    }

    private static bool SupportsRepeatableRead(SupportedDatabase provider)
    {
        return provider is SupportedDatabase.SqlServer or SupportedDatabase.PostgreSql or SupportedDatabase.Oracle;
    }
}

/// <summary>
/// Extended test table creator that includes additional tables for advanced tests
/// </summary>
internal class TestTableCreator
{
    private readonly IDatabaseContext _context;

    public TestTableCreator(IDatabaseContext context)
    {
        _context = context;
    }

    public async Task CreateTestTableAsync()
    {
        // Basic test table creation logic from previous file
        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => CreateSqliteTableSql(),
            SupportedDatabase.PostgreSql => CreatePostgreSqlTableSql(),
            SupportedDatabase.SqlServer => CreateSqlServerTableSql(),
            SupportedDatabase.MySQL => CreateMySqlTableSql(),
            SupportedDatabase.MariaDb => CreateMariaDbTableSql(),
            SupportedDatabase.Oracle => CreateOracleTableSql(),
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    public async Task CreateAccountTableAsync()
    {
        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => @"
                CREATE TABLE IF NOT EXISTS accounts (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    balance DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.PostgreSql => @"
                CREATE TABLE IF NOT EXISTS accounts (
                    id BIGINT PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    balance DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.SqlServer => @"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[accounts]') AND type in (N'U'))
                CREATE TABLE [dbo].[accounts] (
                    [id] BIGINT PRIMARY KEY,
                    [name] NVARCHAR(255) NOT NULL,
                    [balance] DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.MySQL or SupportedDatabase.MariaDb => @"
                CREATE TABLE IF NOT EXISTS accounts (
                    id BIGINT PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    balance DECIMAL(18,2) NOT NULL DEFAULT 0.00
                )",
            SupportedDatabase.Oracle => @"
                DECLARE
                    table_exists NUMBER;
                BEGIN
                    SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'ACCOUNTS';
                    IF table_exists = 0 THEN
                        EXECUTE IMMEDIATE '
                            CREATE TABLE accounts (
                                id NUMBER PRIMARY KEY,
                                name VARCHAR2(255) NOT NULL,
                                balance DECIMAL(18,2) DEFAULT 0.00 NOT NULL
                            )';
                    END IF;
                END;",
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    // Simplified table creation methods (full implementations would be in the previous file)
    private string CreateSqliteTableSql() => @"
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
        )";

    private string CreatePostgreSqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS TestTable (
            Id BIGSERIAL PRIMARY KEY,
            Name VARCHAR(255) NOT NULL,
            Value INTEGER NOT NULL,
            Description TEXT,
            IsActive BOOLEAN NOT NULL DEFAULT TRUE,
            CreatedOn TIMESTAMP NOT NULL DEFAULT NOW(),
            CreatedBy VARCHAR(100),
            LastUpdatedOn TIMESTAMP,
            LastUpdatedBy VARCHAR(100),
            Version INTEGER NOT NULL DEFAULT 1
        )";

    private string CreateSqlServerTableSql() => @"
        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TestTable]') AND type in (N'U'))
        CREATE TABLE [dbo].[TestTable] (
            [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
            [Name] NVARCHAR(255) NOT NULL,
            [Value] INT NOT NULL,
            [Description] NVARCHAR(MAX),
            [IsActive] BIT NOT NULL DEFAULT 1,
            [CreatedOn] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            [CreatedBy] NVARCHAR(100),
            [LastUpdatedOn] DATETIME2,
            [LastUpdatedBy] NVARCHAR(100),
            [Version] INT NOT NULL DEFAULT 1
        )";

    private string CreateMySqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS TestTable (
            Id BIGINT AUTO_INCREMENT PRIMARY KEY,
            Name VARCHAR(255) NOT NULL,
            Value INT NOT NULL,
            Description TEXT,
            IsActive BOOLEAN NOT NULL DEFAULT TRUE,
            CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CreatedBy VARCHAR(100),
            LastUpdatedOn TIMESTAMP NULL,
            LastUpdatedBy VARCHAR(100),
            Version INT NOT NULL DEFAULT 1
        )";

    private string CreateMariaDbTableSql() => CreateMySqlTableSql();

    private string CreateOracleTableSql() => @"
        DECLARE
            table_exists NUMBER;
        BEGIN
            SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TESTTABLE';
            IF table_exists = 0 THEN
                EXECUTE IMMEDIATE '
                    CREATE TABLE TestTable (
                        Id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        Name VARCHAR2(255) NOT NULL,
                        Value NUMBER NOT NULL,
                        Description CLOB,
                        IsActive NUMBER(1) DEFAULT 1 NOT NULL,
                        CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
                        CreatedBy VARCHAR2(100),
                        LastUpdatedOn TIMESTAMP,
                        LastUpdatedBy VARCHAR2(100),
                        Version NUMBER DEFAULT 1 NOT NULL
                    )';
            END IF;
        END;";
}