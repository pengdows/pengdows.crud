using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.strategies.connection;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests.strategies;

public class SingleWriterConnectionStrategyTests
{
    private static DatabaseContext CreateSingleWriterContext()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.SingleWriter
        };
        return new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);
    }

    [Fact]
    public void GetConnection_Write_ReturnsSharedPersistentConnection()
    {
        // VERIFICATION: SingleWriterConnectionStrategy.GetConnection() for writes
        // returns the persistent writer connection which SHOULD be marked as shared

        using var context = CreateSingleWriterContext();

        // The persistent connection is created with isShared: true in InitializeInternals
        var persistentConn = context.PersistentConnection;
        Assert.NotNull(persistentConn);

        var strategy = new SingleWriterConnectionStrategy(context);

        // Request a write connection with isShared = true
        var writeConnSharedTrue = strategy.GetConnection(ExecutionType.Write, true);

        // Request a write connection with isShared = false
        var writeConnSharedFalse = strategy.GetConnection(ExecutionType.Write, false);

        // Both requests return the same persistent connection (parameter ignored)
        Assert.Same(persistentConn, writeConnSharedTrue);
        Assert.Same(persistentConn, writeConnSharedFalse);
        Assert.Same(writeConnSharedTrue, writeConnSharedFalse);

        // CRITICAL CHECK: The persistent connection SHOULD be marked as shared
        // because it's the single writer connection used by all write operations
        var locker = persistentConn.GetLock();

        // If properly configured, should be RealAsyncLocker (not NoOpAsyncLocker.Instance)
        // This provides serialized access to the single writer connection
        var isProperlyShared = !ReferenceEquals(locker, NoOpAsyncLocker.Instance);

        Assert.True(isProperlyShared,
            "Persistent writer connection should be marked as shared (isShared: true) " +
            "to provide serialized access via RealAsyncLocker, not NoOpAsyncLocker.Instance");
    }

    [Fact]
    public void GetConnection_Read_HardcodesIsSharedToFalse()
    {
        // INVESTIGATION: SingleWriterConnectionStrategy.GetConnection() hardcodes isShared = false
        // for read operations, ignoring the caller's isShared parameter

        using var context = CreateSingleWriterContext();

        var strategy = new SingleWriterConnectionStrategy(context);
        strategy.PostInitialize(context.GetSingleConnection());

        // Request a read connection with isShared = true
        var readConnSharedTrue = strategy.GetConnection(ExecutionType.Read, true);

        // Request a read connection with isShared = false
        var readConnSharedFalse = strategy.GetConnection(ExecutionType.Read, false);

        // PROOF OF ISSUE: Both connections use NoOpAsyncLocker (no serialization)
        // because GetConnection() calls GetStandardConnection(isShared: false, readOnly: true)
        // The caller's isShared parameter is ignored

        var lock1 = readConnSharedTrue.GetLock();
        var lock2 = readConnSharedFalse.GetLock();

        // Both should be NoOpAsyncLocker.Instance since isShared was hardcoded to false
        Assert.Same(NoOpAsyncLocker.Instance, lock1);
        Assert.Same(NoOpAsyncLocker.Instance, lock2);

        // Clean up
        readConnSharedTrue.Dispose();
        readConnSharedFalse.Dispose();
    }

    [Fact]
    public void GetConnection_Read_CallerExpectingSerializationGetsNone()
    {
        // CRITICAL ISSUE: If a caller explicitly requests isShared = true for serialization,
        // they silently get a non-shared connection with no serialization

        using var context = CreateSingleWriterContext();

        var strategy = new SingleWriterConnectionStrategy(context);
        strategy.PostInitialize(context.GetSingleConnection());

        // Caller explicitly requests shared connection expecting serialization
        var readConn = strategy.GetConnection(ExecutionType.Read, true);

        // EXPECTATION: If isShared = true, caller expects RealAsyncLocker with semaphore
        // REALITY: Gets NoOpAsyncLocker.Instance (no serialization)

        var locker = readConn.GetLock();

        // This assertion FAILS if the contract is that isShared=true means serialized access
        // It PASSES currently, proving the parameter is ignored
        Assert.Same(NoOpAsyncLocker.Instance, locker);

        // QUESTION: Is this intentional design (strategy knows best)
        // or a contract violation (caller's expectation ignored)?

        readConn.Dispose();
    }

    [Fact]
    public void SingleWriterStrategy_BehaviorIsCorrect()
    {
        // COMPREHENSIVE TEST: Verify SingleWriterConnectionStrategy correctly implements:
        // 1. Write operations use the persistent shared writer connection (serialized access)
        // 2. Read operations use ephemeral non-shared connections (no serialization needed)

        using var context = CreateSingleWriterContext();
        var strategy = new SingleWriterConnectionStrategy(context);

        var persistentConn = context.PersistentConnection;
        Assert.NotNull(persistentConn);

        // WRITE OPERATIONS: Should return shared persistent connection
        var writeConn1 = strategy.GetConnection(ExecutionType.Write, true);
        var writeConn2 = strategy.GetConnection(ExecutionType.Write, false);

        Assert.Same(persistentConn, writeConn1);
        Assert.Same(persistentConn, writeConn2);

        // Verify persistent connection has RealAsyncLocker (serialized access)
        var writeLocker = persistentConn.GetLock();
        Assert.NotSame(NoOpAsyncLocker.Instance, writeLocker);

        // READ OPERATIONS: Should return ephemeral non-shared connections
        var readConn1 = strategy.GetConnection(ExecutionType.Read, true);
        var readConn2 = strategy.GetConnection(ExecutionType.Read, false);

        // Reads are different instances (ephemeral)
        Assert.NotSame(readConn1, readConn2);
        Assert.NotSame(persistentConn, readConn1);
        Assert.NotSame(persistentConn, readConn2);

        // Read connections use NoOpAsyncLocker (no serialization needed)
        var readLocker1 = readConn1.GetLock();
        var readLocker2 = readConn2.GetLock();
        Assert.Same(NoOpAsyncLocker.Instance, readLocker1);
        Assert.Same(NoOpAsyncLocker.Instance, readLocker2);

        // Clean up ephemeral read connections
        readConn1.Dispose();
        readConn2.Dispose();

        // CONCLUSION: The strategy correctly provides:
        // - Serialized access to the single writer (via RealAsyncLocker)
        // - Non-serialized ephemeral read connections (via NoOpAsyncLocker)
        // - The isShared parameter is intentionally ignored because the strategy
        //   knows better than the caller what type of connection is appropriate
    }
}