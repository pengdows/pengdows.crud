using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SessionInitializationFailureModeTests
{
    [Fact]
    public async Task OpenAsync_FailClosed_SessionSettingsFailure_ThrowsConnectionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // First connection: used for DatabaseContext construction/detection — must succeed.
        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        // Second connection: used for the actual operation. CreateCommand() (used both by
        // session-settings application AND by ordinary query execution) fails.
        var opConnection = new fakeDbConnection();
        opConnection.SetFailOnCommand();
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed
        };

        await using var context = new DatabaseContext(config, factory);

        // Internal accessor — exercises the async first-open path, where the double-swallow
        // bug lived (a second, redundant try/catch around ExecuteSessionSettingsAsync).
        var conn = context.GetConnection(ExecutionType.Write);
        try
        {
            await Assert.ThrowsAsync<ConnectionException>(async () => await conn.OpenAsync());
        }
        finally
        {
            conn.Dispose();
        }
    }

    [Fact]
    public async Task OpenAsync_BestEffort_SessionSettingsFailure_DoesNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        var opConnection = new fakeDbConnection();
        opConnection.SetFailOnCommand();
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
            // SessionInitializationFailureMode left at its default (BestEffort) — regression guard.
        };

        await using var context = new DatabaseContext(config, factory);

        var conn = context.GetConnection(ExecutionType.Write);
        try
        {
            var ex = await Record.ExceptionAsync(async () => await conn.OpenAsync());
            Assert.Null(ex);
        }
        finally
        {
            conn.Dispose();
        }
    }

    [Fact]
    public async Task BeginTransaction_FailClosed_SessionSettingsFailure_DoesNotLeakPoolSlot()
    {
        // Regression: OpenConnectionWithOptionalLock (TransactionContext.cs) calls connection.Open()
        // with no surrounding try/catch. Under FailClosed, ExecuteSessionSettings throws
        // ConnectionException from inside Open() *before* BeginTransaction's own
        // try { transaction = connection.BeginTransaction(); } catch { gate.Dispose();
        // context.CloseAndDisposeConnection(connection); } block ever starts — leaking the
        // connection's already-acquired PoolGovernor write slot. Standard mode's write governor
        // has exactly one slot, so a leaked slot makes every subsequent write hang until
        // PoolAcquireTimeout, then throw PoolSaturatedException, instead of succeeding immediately.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        var firstOpConnection = new fakeDbConnection();
        firstOpConnection.SetFailOnCommand();
        factory.Connections.Add(firstOpConnection);

        var secondOpConnection = new fakeDbConnection();
        factory.Connections.Add(secondOpConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed,
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(500)
        };

        await using var context = new DatabaseContext(config, factory);

        await Assert.ThrowsAsync<ConnectionException>(async () =>
        {
            using var tx = context.BeginTransaction();
        });

        // If the write slot leaked above, this call blocks for PoolAcquireTimeout and throws
        // PoolSaturatedException instead of succeeding immediately.
        using var second = context.BeginTransaction();
        second.Commit();
    }

    [Fact]
    public void Constructor_SingleConnection_FailClosed_SessionSettingsFailure_DisposesPersistentConnection()
    {
        // Regression: unlike Standard/SingleWriter, DbMode.SingleConnection applies session
        // settings synchronously INSIDE the constructor (DatabaseContext.Initialization.cs),
        // against the same physical connection that becomes PersistentConnection — there is no
        // separate "detection connection" vs "operation connection" split like Standard mode has.
        // Under FailClosed, that call throws ConnectionException from inside the constructor, but
        // the constructor's outer catch block only unregistered the (unrelated) duplicate-
        // connection-string warning registration — it never disposed PersistentConnection, which
        // was already open. Since a failed constructor never returns an object for the caller to
        // Dispose, that connection leaked on every SingleConnection + FailClosed session-settings
        // failure.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // Phase 1: learn how many commands a normal, successful SingleConnection construction
        // creates on its one physical connection, so phase 2 can fail on exactly the last one
        // (the session-settings SET) without hardcoding an internal command count that could
        // shift if construction internals change.
        var probeConnection = new fakeDbConnection();
        factory.Connections.Add(probeConnection);
        var successfulCommandCount = 0;
        probeConnection.SetCustomCommandBehavior(() => successfulCommandCount++);
        var probeConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using (new DatabaseContext(probeConfig, factory))
        {
        }

        Assert.True(successfulCommandCount > 0);

        // Phase 2: same construction, but the LAST command (session-settings application) fails.
        var failingConnection = new fakeDbConnection();
        factory.Connections.Add(failingConnection);
        var commandCount = 0;
        failingConnection.SetCustomCommandBehavior(() =>
        {
            commandCount++;
            if (commandCount == successfulCommandCount)
            {
                throw new InvalidOperationException("Simulated session-settings command failure");
            }
        });

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed
        };

        Assert.Throws<ConnectionException>(() => new DatabaseContext(config, factory));

        Assert.True(failingConnection.DisposeCount > 0);
    }
}
