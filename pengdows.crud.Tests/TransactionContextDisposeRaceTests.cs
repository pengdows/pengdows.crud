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
}
