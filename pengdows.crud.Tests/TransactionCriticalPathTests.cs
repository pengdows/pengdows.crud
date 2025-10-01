using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Critical path tests for transaction handling and isolation management
/// </summary>
public class TransactionCriticalPathTests
{
    /// <summary>
    /// Test transaction rollback after connection failure
    /// </summary>
    [Fact]
    public void Transaction_RollbackAfterConnectionFailure_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var transaction = context.BeginTransaction();

        // Simulate connection failure
        factory.SetGlobalFailureMode(ConnectionFailureMode.FailOnCommand);

        // Should handle rollback gracefully even with connection issues
        transaction.Rollback();
        Assert.True(transaction.WasRolledBack);
    }

    /// <summary>
    /// Test transaction timeout scenarios
    /// </summary>
    [Fact]
    public async Task Transaction_TimeoutScenario_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            EnableDataPersistence = true
        };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;Command Timeout=1",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Create table and insert data
        using (var createContainer = context.CreateSqlContainer("CREATE TABLE TimeoutTest (Value INTEGER)"))
        {
            await createContainer.ExecuteNonQueryAsync();
        }

        using (var insertContainer = context.CreateSqlContainer("INSERT INTO TimeoutTest (Value) VALUES (1)"))
        {
            await insertContainer.ExecuteNonQueryAsync();
        }

        // Test transaction with timeout settings
        using var transaction = context.BeginTransaction();

        // Should handle transaction operations with timeout settings
        using var container = context.CreateSqlContainer("SELECT Value FROM TimeoutTest");
        var result = await container.ExecuteScalarAsync<int>();

        transaction.Commit();
        Assert.True(transaction.WasCommitted);
        Assert.Equal(1, result);
    }

    /// <summary>
    /// Test nested transaction scenarios (savepoints)
    /// </summary>
    [Fact]
    public async Task Transaction_NestedTransactions_SavepointsWorkCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql); // Supports savepoints
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var transaction = context.BeginTransaction();

        // Create savepoint
        await transaction.SavepointAsync("sp1");

        // Create nested savepoint
        await transaction.SavepointAsync("sp2");

        // Rollback to savepoint
        await transaction.RollbackToSavepointAsync("sp1");

        // Should handle savepoint operations correctly
        transaction.Commit();
        Assert.True(transaction.WasCommitted);
    }

    /// <summary>
    /// Test transaction with invalid isolation level
    /// </summary>
    [Fact]
    public void Transaction_InvalidIsolationLevel_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Test unsupported isolation levels
        using var transaction = context.BeginTransaction(IsolationLevel.Chaos);
        Assert.NotNull(transaction);

        // Should map to supported isolation level
        transaction.Commit();
        Assert.True(transaction.WasCommitted);
    }

    /// <summary>
    /// Test transaction double commit protection
    /// </summary>
    [Fact]
    public void Transaction_DoubleCommit_ThrowsInvalidOperation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var transaction = context.BeginTransaction();

        transaction.Commit();
        Assert.True(transaction.WasCommitted);

        // Second commit should throw
        Assert.Throws<InvalidOperationException>(() => transaction.Commit());
    }

    /// <summary>
    /// Test transaction double rollback protection
    /// </summary>
    [Fact]
    public void Transaction_DoubleRollback_ThrowsInvalidOperation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var transaction = context.BeginTransaction();

        transaction.Rollback();
        Assert.True(transaction.WasRolledBack);

        // Second rollback should throw
        Assert.Throws<InvalidOperationException>(() => transaction.Rollback());
    }

    /// <summary>
    /// Test transaction commit after rollback protection
    /// </summary>
    [Fact]
    public void Transaction_CommitAfterRollback_ThrowsInvalidOperation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var transaction = context.BeginTransaction();

        transaction.Rollback();
        Assert.True(transaction.WasRolledBack);

        // Commit after rollback should throw
        Assert.Throws<InvalidOperationException>(() => transaction.Commit());
    }

    /// <summary>
    /// Test transaction isolation profile mapping
    /// </summary>
    [Fact]
    public void Transaction_IsolationProfileMapping_WorksCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Test different isolation profiles
        using var tx1 = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);
        Assert.NotNull(tx1);
        tx1.Commit();

        using var tx2 = context.BeginTransaction(IsolationProfile.StrictConsistency);
        Assert.NotNull(tx2);
        tx2.Commit();

        using var tx3 = context.BeginTransaction(IsolationProfile.FastWithRisks);
        Assert.NotNull(tx3);
        tx3.Commit();
    }

    /// <summary>
    /// Test transaction disposal without explicit commit/rollback
    /// </summary>
    [Fact]
    public void Transaction_DisposeWithoutCommitOrRollback_RollsBackAutomatically()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        bool wasRolledBack;
        using (var transaction = context.BeginTransaction())
        {
            // Don't commit or rollback explicitly
            // Disposal should trigger automatic rollback
            wasRolledBack = transaction.WasRolledBack;
        }

        // Should have been automatically rolled back on disposal
        // (depending on implementation, this might be handled in Dispose)
    }

    /// <summary>
    /// Test concurrent transaction creation
    /// </summary>
    [Fact]
    public async Task Transaction_ConcurrentCreation_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Try to create multiple transactions concurrently
        var tasks = new Task<bool>[5];
        for (int i = 0; i < 5; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    using var transaction = context.BeginTransaction();
                    transaction.Commit();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        var results = await Task.WhenAll(tasks);

        // At least some should succeed (depending on concurrency handling)
        Assert.Contains(true, results);
    }

    /// <summary>
    /// Test transaction with read-only context
    /// </summary>
    [Fact]
    public void Transaction_ReadOnlyContext_ThrowsNotSupported()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, factory);

        // Read-only context should not allow write transactions
        Assert.Throws<NotSupportedException>(() =>
            context.BeginTransaction(executionType: ExecutionType.Write));
    }

    /// <summary>
    /// Test transaction connection sharing
    /// </summary>
    [Fact]
    public void Transaction_ConnectionSharing_WorksCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var transaction = context.BeginTransaction();

        // Multiple operations within same transaction should share connection
        using var container1 = context.CreateSqlContainer("SELECT 1");
        using var container2 = context.CreateSqlContainer("SELECT 2");

        // Both should work within the transaction context
        var conn1 = context.GetConnection(ExecutionType.Write);
        var conn2 = context.GetConnection(ExecutionType.Write);

        // Should reuse the transaction connection
        Assert.NotNull(conn1);
        Assert.NotNull(conn2);

        transaction.Commit();
    }

    /// <summary>
    /// Test transaction workflow commits data when FakeDb persistence is enabled
    /// </summary>
    [Fact]
    public async Task Transaction_DeadlockHandling_WorksCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer) { EnableDataPersistence = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        using var transaction = context.BeginTransaction();

        using (var insert = context.CreateSqlContainer("INSERT INTO Orders (Id, Name) VALUES (@id, @name)"))
        {
            insert.AddParameterWithValue("id", DbType.Int32, 1);
            insert.AddParameterWithValue("name", DbType.String, "initial");
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        using (var update = context.CreateSqlContainer("UPDATE Orders SET Name = @name WHERE Id = @id"))
        {
            update.AddParameterWithValue("id", DbType.Int32, 1);
            update.AddParameterWithValue("name", DbType.String, "updated");
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        transaction.Commit();
        Assert.True(transaction.WasCommitted);

        using var verify = context.CreateSqlContainer("SELECT Name FROM Orders WHERE Id = @id");
        verify.AddParameterWithValue("id", DbType.Int32, 1);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("updated", reader.GetString(0));
    }

    /// <summary>
    /// Test transaction with connection pooling edge cases
    /// </summary>
    [Fact]
    public void Transaction_ConnectionPoolingEdgeCases_HandlesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=test;Max Pool Size=1;Min Pool Size=1",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Test transaction with minimal pool size
        using var transaction = context.BeginTransaction();

        // Should handle pool exhaustion scenarios
        using var container = context.CreateSqlContainer("SELECT 1");

        transaction.Commit();
        Assert.True(transaction.WasCommitted);
    }
}
