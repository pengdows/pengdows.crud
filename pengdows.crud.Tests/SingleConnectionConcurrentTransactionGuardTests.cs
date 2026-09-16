#region

using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// DbMode.SingleConnection shares one physical connection across the entire context, so only one
/// transaction can be open on it at a time — a hard ADO.NET constraint, not a tunable one.
/// SingleConnection mode is already fully serialized by design (every operation, transactional or
/// not, funnels through the one connection), so a transaction is treated as just a longer-held
/// instance of that same serialization: it acquires a dedicated gate for its whole lifetime
/// (Begin through Commit/Rollback/Dispose), and every other caller — another transaction attempt,
/// or an ordinary non-transactional command — waits its turn on that same gate, bounded by
/// ModeLockTimeout (default 30s) rather than blocking forever.
///
/// Before this fix, nothing serialized transaction-begin at all: a second BeginTransaction()
/// attempt could race directly against the provider (confirmed live: Microsoft.Data.Sqlite throws
/// "SqliteConnection does not support nested transactions"), and — more seriously — an ordinary
/// write executing while another task's transaction was still open could be silently absorbed
/// into that transaction's scope and rolled back with it, a real, confirmed data-loss bug (a
/// 268-vs-252 row-count mismatch under concurrent load in SingleConnectionConcurrencyTortureTests).
/// </summary>
public class SingleConnectionConcurrentTransactionGuardTests
{
    private static DatabaseContext CreateSingleConnectionContext(TimeSpan? modeLockTimeout = null)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            ModeLockTimeout = modeLockTimeout
        };

        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task BeginTransaction_WhileAnotherIsActive_BlocksThenSucceedsOnceFirstCompletes()
    {
        await using var context = CreateSingleConnectionContext();

        var firstStarted = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var secondAcquired = false;

        var firstTask = Task.Run(async () =>
        {
            using var first = context.BeginTransaction();
            firstStarted.SetResult();
            await releaseFirst.Task;
            first.Commit();
        });

        await firstStarted.Task;

        var secondTask = Task.Run(() =>
        {
            using var second = context.BeginTransaction();
            secondAcquired = true;
            second.Commit();
        });

        // Give the second attempt time to genuinely be blocked, not just not-yet-scheduled.
        await Task.Delay(200);
        Assert.False(secondAcquired, "Second BeginTransaction should still be blocked while the first transaction is open.");

        releaseFirst.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        Assert.True(secondAcquired, "Second BeginTransaction should succeed once the first transaction completes.");
    }

    [Fact]
    public async Task BeginTransaction_WhileAnotherIsActive_ThrowsModeContentionExceptionAfterTimeout()
    {
        await using var context = CreateSingleConnectionContext(TimeSpan.FromMilliseconds(100));
        await using var first = context.BeginTransaction();

        // A different thread genuinely contends for the gate `first` holds — not a self-deadlock,
        // since the semaphore doesn't care about logical call-site identity, only real Wait/Release.
        await Assert.ThrowsAsync<ModeContentionException>(() => Task.Run(() => context.BeginTransaction()));
    }

    [Fact]
    public async Task BeginTransaction_AfterPriorTransactionCommits_SucceedsAgain()
    {
        await using var context = CreateSingleConnectionContext();

        using (var first = context.BeginTransaction())
        {
            first.Commit();
        }

        // Must not still be blocked by the completed transaction.
        await using var second = context.BeginTransaction();
        second.Commit();
    }

    [Fact]
    public async Task BeginTransaction_AfterPriorTransactionRollsBack_SucceedsAgain()
    {
        await using var context = CreateSingleConnectionContext();

        using (var first = context.BeginTransaction())
        {
            first.Rollback();
        }

        await using var second = context.BeginTransaction();
        second.Commit();
    }

    [Fact]
    public async Task BeginTransaction_AfterPriorTransactionDisposedWithoutCompletion_SucceedsAgain()
    {
        await using var context = CreateSingleConnectionContext();

        using (context.BeginTransaction())
        {
            // Disposed without explicit Commit/Rollback — implicit rollback via Dispose.
        }

        await using var second = context.BeginTransaction();
        second.Commit();
    }

    [Fact]
    public async Task OrdinaryWrite_WhileTransactionIsActive_BlocksThenSucceedsOnceTransactionCompletes()
    {
        // The actual data-loss scenario: an ordinary (non-transactional) write must wait behind an
        // active transaction rather than executing concurrently and risking silent absorption into
        // its scope.
        await using var context = CreateSingleConnectionContext();

        var txStarted = new TaskCompletionSource();
        var releaseTx = new TaskCompletionSource();
        var writeCompleted = false;

        var txTask = Task.Run(async () =>
        {
            using var tx = context.BeginTransaction();
            txStarted.SetResult();
            await releaseTx.Task;
            tx.Commit();
        });

        await txStarted.Task;

        var writeTask = Task.Run(async () =>
        {
            var sc = context.CreateSqlContainer("UPDATE Foo SET Bar = 1");
            await sc.ExecuteNonQueryAsync();
            writeCompleted = true;
        });

        await Task.Delay(200);
        Assert.False(writeCompleted, "Ordinary operation should still be blocked while the transaction is open.");

        releaseTx.SetResult();
        await Task.WhenAll(txTask, writeTask);

        Assert.True(writeCompleted, "Ordinary operation should succeed once the transaction completes.");
    }
}
