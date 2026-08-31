using System;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Regression tests for two related TransactionContext findings from the PoolGovernor/
/// TransactionContext audit:
///
/// - CommitAsync/RollbackAsync previously called the transaction's plain sync Commit()/
///   Rollback() under an async facade, never touching DbTransaction's own CommitAsync/
///   RollbackAsync (so a provider with a genuinely async, cancellable implementation never got
///   to use it, and the cancellationToken parameter was accepted but silently ignored by the
///   actual commit/rollback call).
/// - CompleteTransaction/CompleteTransactionAsync wrapped ANY exception from that call —
///   including OperationCanceledException — into TransactionException, violating the
///   documented, otherwise-universal contract that OperationCanceledException is never wrapped.
/// </summary>
[Collection("SqliteSerial")]
public class TransactionContextCommitRollbackBugFixTests
{
    private static (DatabaseContext Context, TransactionContext Transaction, fakeDbTransaction FakeTransaction) CreateOpenTransaction()
    {
        var context = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));

        var txn = (TransactionContext)context.BeginTransaction();
        var transactionField = typeof(TransactionContext).GetField("_transaction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var fakeTransaction = (fakeDbTransaction)transactionField!.GetValue(txn)!;

        return (context, txn, fakeTransaction);
    }

    [Fact]
    public async Task CommitAsync_UsesTransactionsCommitAsync_NotJustSyncCommit()
    {
        var (context, txn, fakeTransaction) = CreateOpenTransaction();
        using var _ = context;

        await txn.CommitAsync();

        Assert.Equal(1, fakeTransaction.CommitAsyncCallCount);
        Assert.True(txn.WasCommitted);
    }

    [Fact]
    public async Task RollbackAsync_UsesTransactionsRollbackAsync_NotJustSyncRollback()
    {
        var (context, txn, fakeTransaction) = CreateOpenTransaction();
        using var _ = context;

        await txn.RollbackAsync();

        Assert.Equal(1, fakeTransaction.RollbackAsyncCallCount);
        Assert.True(txn.WasRolledBack);
    }

    [Fact]
    public async Task CommitAsync_WhenCommitThrowsOperationCanceled_PropagatesUnwrapped()
    {
        var (context, txn, fakeTransaction) = CreateOpenTransaction();
        using var _ = context;
        fakeTransaction.CommitException = new OperationCanceledException("simulated cancellation");

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await txn.CommitAsync());
    }

    [Fact]
    public async Task RollbackAsync_WhenRollbackThrowsOperationCanceled_PropagatesUnwrapped()
    {
        var (context, txn, fakeTransaction) = CreateOpenTransaction();
        using var _ = context;
        fakeTransaction.RollbackException = new OperationCanceledException("simulated cancellation");

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await txn.RollbackAsync());
    }

    [Fact]
    public async Task CommitAsync_WhenCommitThrowsOrdinaryException_StillWrapsInTransactionException()
    {
        // Guards against an overly-broad fix: only OperationCanceledException should bypass the
        // TransactionException wrapper — everything else must still be wrapped as before.
        var (context, txn, fakeTransaction) = CreateOpenTransaction();
        using var _ = context;
        fakeTransaction.CommitException = new InvalidOperationException("boom");

        var ex = await Assert.ThrowsAsync<pengdows.crud.exceptions.TransactionException>(async () => await txn.CommitAsync());
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }
}
