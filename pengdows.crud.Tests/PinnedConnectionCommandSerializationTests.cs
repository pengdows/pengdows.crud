using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// §1/Q2 (CONCURRENCY.md): "Concurrent commands on one checkout are described in comments as
/// serialized by a per-connection lock... Assumption; Test: pending." Traced instead of assumed:
/// TrackedConnection.GetLock() (wrappers/TrackedConnection.cs) returns a real lock
/// (RealAsyncLocker over a SemaphoreSlim) only when the connection is shared (_isSharedConnection
/// -- a transaction's pinned connection, or SingleConnection's persistent one); for an ordinary
/// ephemeral per-operation connection it returns NoOpAsyncLocker, because no contention is
/// possible by construction (each operation already has its own distinct physical connection).
/// So the claim only has content for a shared/pinned connection -- a transaction is the
/// straightforward case to test.
///
/// SqlContainer's own execute path acquires this lock via connectionLocker.LockAsync(...)
/// (ExecuteReaderAsyncInternal/ExecuteNonQueryAsync) before creating or running the command, so a
/// second command racing in on the same pinned connection while the first is still mid-execution
/// must genuinely wait for the lock, not interleave.
/// </summary>
public sealed class PinnedConnectionCommandSerializationTests
{
    [Fact(Timeout = 10000)]
    public async Task TwoCommands_OnTheSameTransactionsPinnedConnection_AreGenuinelySerialized_NotInterleaved()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(config, factory);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.EnqueueNonQueryResult(1);
        conn.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn);

        using var txn = ctx.BeginTransaction();

        var gate = conn.SetExecuteGate();
        using var scA = txn.CreateSqlContainer("UPDATE a SET x = 1");
        var taskA = scA.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None).AsTask();
        await conn.ExecuteGateEntered;

        // A is now parked mid-execute, holding the connection lock it acquired before even
        // reaching the gate. A second command on the SAME pinned connection must therefore be
        // unable to proceed -- not run concurrently, not reordered ahead of A.
        using var scB = txn.CreateSqlContainer("UPDATE b SET y = 2");
        var taskB = scB.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None).AsTask();

        await Task.Delay(100);
        Assert.False(taskA.IsCompleted, "A should still be parked at the gate.");
        Assert.False(taskB.IsCompleted,
            "B must genuinely wait for A's connection lock, not race ahead of it while A is mid-execute.");
        Assert.DoesNotContain("UPDATE b SET y = 2", conn.ExecutedNonQueryTexts);

        // Release A -- only now should B be able to acquire the lock and run.
        gate.SetResult(true);

        await taskA;
        await taskB;

        // The decisive, order-sensitive proof: B's text must not appear until after A's command
        // actually ran, confirming real serialization rather than two results that merely both
        // eventually completed.
        var indexOfA = conn.ExecutedNonQueryTexts.IndexOf("UPDATE a SET x = 1");
        var indexOfB = conn.ExecutedNonQueryTexts.IndexOf("UPDATE b SET y = 2");
        Assert.True(indexOfA >= 0 && indexOfB >= 0 && indexOfA < indexOfB,
            $"Expected A to execute strictly before B; order was: {string.Join(", ", conn.ExecutedNonQueryTexts)}");
    }

    [Fact(Timeout = 10000)]
    public async Task TwoCommands_OnSeparateStandardModeConnections_AreNotArtificiallySerialized()
    {
        // The contrasting case: Standard mode gives each operation its own ephemeral connection,
        // so GetLock() returns NoOpAsyncLocker -- there is nothing to serialize because there is
        // no shared resource. This is the control that proves the lock in the test above is doing
        // real work, not just slow scheduling.
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            MaxConcurrentWrites = 2
        };
        using var ctx = new DatabaseContext(config, factory);

        var connA = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        var gate = connA.SetExecuteGate();
        factory.Connections.Add(connA);

        var connB = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        connB.EnqueueNonQueryResult(1);
        factory.Connections.Add(connB);

        using var scA = ctx.CreateSqlContainer("UPDATE a SET x = 1");
        var taskA = scA.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, CancellationToken.None).AsTask();
        await connA.ExecuteGateEntered;

        using var scB = ctx.CreateSqlContainer("UPDATE b SET y = 2");
        var taskB = scB.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, CancellationToken.None).AsTask();

        var completedInTime = await Task.WhenAny(taskB, Task.Delay(TimeSpan.FromSeconds(5))) == taskB;
        Assert.True(completedInTime,
            "B uses a separate ephemeral connection and must complete independently of A, which " +
            "is still parked at its own gate.");

        gate.SetResult(true);
        await taskA;
    }
}
