using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that concurrent Dispose and Rollback/Commit on a TransactionContext
/// do not produce ObjectDisposedException or leave connections leaked.
/// </summary>
public class TransactionContextDisposeRaceTests
{
    private static DatabaseContext BuildContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        return new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
    }

    /// <summary>
    /// When Dispose races with an in-progress Rollback/Commit:
    /// - Dispose sees Wait(0) fail (lock held by the completing thread)
    /// - The completing thread finishes and calls _completionLock.Release()
    /// - That Release() must NOT throw ObjectDisposedException even though
    ///   Dispose already called _completionLock.Dispose().
    ///
    /// We simulate this by acquiring _completionLock externally via reflection
    /// before calling Dispose, then releasing it afterwards and verifying no
    /// exception escapes from Release().
    /// </summary>
    [Fact]
    public void TransactionContext_Dispose_WhenLockHeld_DoesNotThrowAndReleaseSurvives()
    {
        using var ctx = BuildContext();
        using var txn = ctx.BeginTransaction();

        // Acquire the internal completion lock to simulate a concurrent Rollback in progress.
        var lockField = typeof(TransactionContext).GetField(
            "_completionLock",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lockField);
        var sem = (SemaphoreSlim)lockField!.GetValue(txn)!;
        sem.Wait(); // hold it — simulates Thread A mid-completion

        // Dispose while lock is held — must not throw even though rollback is skipped.
        var disposeEx = Record.Exception(() => ((IDisposable)txn).Dispose());
        Assert.Null(disposeEx);

        // Thread A's Release() after Dispose — must NOT throw ObjectDisposedException.
        // Before the fix, _completionLock.Dispose() in Dispose() caused this to throw.
        var releaseEx = Record.Exception(() => sem.Release());
        Assert.Null(releaseEx);
    }

    /// <summary>
    /// Same race scenario but through the async DisposeAsync path.
    /// </summary>
    [Fact]
    public async Task TransactionContext_DisposeAsync_WhenLockHeld_DoesNotThrowAndReleaseSurvives()
    {
        await using var ctx = BuildContext();
        var txn = await ctx.BeginTransactionAsync();

        var lockField = typeof(TransactionContext).GetField(
            "_completionLock",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lockField);
        var sem = (SemaphoreSlim)lockField!.GetValue(txn)!;
        sem.Wait();

        var disposeEx = await Record.ExceptionAsync(async () => await txn.DisposeAsync());
        Assert.Null(disposeEx);

        var releaseEx = Record.Exception(() => sem.Release());
        Assert.Null(releaseEx);
    }

    /// <summary>
    /// Normal (non-racing) Dispose: must not throw and must mark the transaction completed.
    /// Verifies baseline behaviour is not broken by the fix.
    /// </summary>
    [Fact]
    public void TransactionContext_Dispose_Normal_CompletesCleanly()
    {
        using var ctx = BuildContext();
        var txn = ctx.BeginTransaction();

        var ex = Record.Exception(() => ((IDisposable)txn).Dispose());
        Assert.Null(ex);

        // After disposal the transaction must report IsCompleted.
        Assert.True(txn.IsCompleted, "Transaction must be completed after disposal.");
    }

    // =========================================================================
    // Commit/Rollback (sync + async) racing a concurrent Dispose/DisposeAsync.
    //
    // Regression: DisposeManaged/DisposeManagedAsync called _transaction.Dispose()
    // unconditionally, even when _completionLock.Wait(0) failed because another thread was
    // actively inside _transaction.Commit()/Rollback() — a dispose-while-in-use race. The fix
    // moves disposal into CompleteTransaction's/CompleteTransactionAsync's finally block
    // (guaranteed to run exactly once), so whichever thread actually completes the transaction
    // is the one that disposes it — never a concurrent Dispose() that lost the lock race.
    //
    // Each test below proves ordering, not just a final count: the transaction must NOT be
    // disposed while Commit/Rollback is still blocked mid-flight, and must be disposed exactly
    // once after it finishes.
    // =========================================================================

    private static fakeDbTransaction GetFakeTransaction(pengdows.crud.ITransactionContext txn)
    {
        return (fakeDbTransaction)((TransactionContext)txn).Transaction;
    }

    [Fact]
    public async Task Commit_ConcurrentDispose_TransactionNotDisposedUntilCommitCompletes()
    {
        using var ctx = BuildContext();
        var txn = ctx.BeginTransaction();
        var fakeTx = GetFakeTransaction(txn);
        fakeTx.CommitGate = new ManualResetEventSlim(false);
        fakeTx.CommitStarted = new ManualResetEventSlim(false);

        var commitTask = Task.Run(() => txn.Commit());
        Assert.True(fakeTx.CommitStarted.Wait(TimeSpan.FromSeconds(5)), "Commit did not start in time.");

        var disposeTask = Task.Run(() => ((IDisposable)txn).Dispose());
        await Task.Delay(50); // give Dispose a chance to (wrongly) run while Commit is blocked

        Assert.Equal(0, fakeTx.DisposeCallCount);

        fakeTx.CommitGate.Set();
        await commitTask;
        await disposeTask;

        Assert.Equal(1, fakeTx.DisposeCallCount);
        Assert.Equal(1, fakeTx.CommitCallCount);
    }

    [Fact]
    public async Task Rollback_ConcurrentDispose_TransactionNotDisposedUntilRollbackCompletes()
    {
        using var ctx = BuildContext();
        var txn = ctx.BeginTransaction();
        var fakeTx = GetFakeTransaction(txn);
        fakeTx.RollbackGate = new ManualResetEventSlim(false);
        fakeTx.RollbackStarted = new ManualResetEventSlim(false);

        var rollbackTask = Task.Run(() => txn.Rollback());
        Assert.True(fakeTx.RollbackStarted.Wait(TimeSpan.FromSeconds(5)), "Rollback did not start in time.");

        var disposeTask = Task.Run(() => ((IDisposable)txn).Dispose());
        await Task.Delay(50);

        Assert.Equal(0, fakeTx.DisposeCallCount);

        fakeTx.RollbackGate.Set();
        await rollbackTask;
        await disposeTask;

        Assert.Equal(1, fakeTx.DisposeCallCount);
        Assert.Equal(1, fakeTx.RollbackCallCount);
    }

    [Fact]
    public async Task CommitAsync_ConcurrentDisposeAsync_TransactionNotDisposedUntilCommitCompletes()
    {
        await using var ctx = BuildContext();
        var txn = await ctx.BeginTransactionAsync();
        var fakeTx = GetFakeTransaction(txn);
        fakeTx.CommitGate = new ManualResetEventSlim(false);
        fakeTx.CommitStarted = new ManualResetEventSlim(false);

        var commitTask = Task.Run(() => txn.CommitAsync().AsTask());
        Assert.True(fakeTx.CommitStarted.Wait(TimeSpan.FromSeconds(5)), "CommitAsync did not start in time.");

        var disposeTask = Task.Run(() => txn.DisposeAsync().AsTask());
        await Task.Delay(50);

        Assert.Equal(0, fakeTx.DisposeCallCount);

        fakeTx.CommitGate.Set();
        await commitTask;
        await disposeTask;

        Assert.Equal(1, fakeTx.DisposeCallCount);
        Assert.Equal(1, fakeTx.CommitCallCount);
    }

    [Fact]
    public async Task RollbackAsync_ConcurrentDisposeAsync_TransactionNotDisposedUntilRollbackCompletes()
    {
        await using var ctx = BuildContext();
        var txn = await ctx.BeginTransactionAsync();
        var fakeTx = GetFakeTransaction(txn);
        fakeTx.RollbackGate = new ManualResetEventSlim(false);
        fakeTx.RollbackStarted = new ManualResetEventSlim(false);

        var rollbackTask = Task.Run(() => txn.RollbackAsync().AsTask());
        Assert.True(fakeTx.RollbackStarted.Wait(TimeSpan.FromSeconds(5)), "RollbackAsync did not start in time.");

        var disposeTask = Task.Run(() => txn.DisposeAsync().AsTask());
        await Task.Delay(50);

        Assert.Equal(0, fakeTx.DisposeCallCount);

        fakeTx.RollbackGate.Set();
        await rollbackTask;
        await disposeTask;

        Assert.Equal(1, fakeTx.DisposeCallCount);
        Assert.Equal(1, fakeTx.RollbackCallCount);
    }

    /// <summary>
    /// Normal (non-racing) commit: the transaction must be disposed exactly once — not once by
    /// Commit's own completion path and again later when Dispose() runs on an already-completed
    /// transaction (the pre-fix code disposed unconditionally in Dispose regardless of
    /// IsCompleted, a redundant double-dispose in the non-racing case too).
    /// </summary>
    [Fact]
    public void Commit_ThenDispose_DisposesTransactionExactlyOnce()
    {
        using var ctx = BuildContext();
        var txn = ctx.BeginTransaction();
        var fakeTx = GetFakeTransaction(txn);

        txn.Commit();
        Assert.Equal(1, fakeTx.DisposeCallCount);

        ((IDisposable)txn).Dispose();
        Assert.Equal(1, fakeTx.DisposeCallCount);
    }
}
