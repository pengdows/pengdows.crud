using System;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// -------------------------------------------------------------------------
// Regression: a failed commit is reported to metrics as a rollback.
//
// CompleteTransactionMetrics (TransactionContext.cs) branches purely on
// "_committed == 1 ? TransactionCommitted() : TransactionRolledBack()". When the underlying
// commit throws, _committed stays 0 (never set — the throw happens before the
// Interlocked.Exchange that would mark it) and _rolledBack ALSO stays 0 (no rollback was ever
// attempted or requested), but _metricsCompleted still flips to 1 in the finally block that
// runs regardless of the exception. CompleteTransactionMetrics sees only "_committed != 1" and
// unconditionally records TransactionRolledBack — even though WasRolledBack correctly reports
// false at the same moment. A caller's public API (WasCommitted/WasRolledBack) and its
// metrics/telemetry disagree about what happened.
// -------------------------------------------------------------------------
[Collection("SqliteSerial")]
public class TransactionContextCommitMetricsBugFixTests
{
    // Metrics are opt-in (DatabaseContextConfiguration.EnableMetrics defaults to false — see
    // DatabaseContext.Initialization.cs). A dedicated helper opts in so the metrics-specific
    // test below gets a context whose Metrics property actually reflects transaction activity
    // instead of the all-zero snapshot CreateMetricsSnapshot returns when _metricsCollector is
    // null.
    private static (DatabaseContext Context, TransactionContext Transaction, fakeDbTransaction FakeTransaction) CreateOpenTransactionWithMetrics()
    {
        var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
                EnableMetrics = true
            },
            new fakeDbFactory(SupportedDatabase.Sqlite));

        var txn = (TransactionContext)context.BeginTransaction();
        var transactionField = typeof(TransactionContext).GetField("_transaction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var fakeTransaction = (fakeDbTransaction)transactionField!.GetValue(txn)!;

        return (context, txn, fakeTransaction);
    }

    [Fact]
    public async Task CommitAsync_WhenCommitThrows_DoesNotReportRolledBackInMetrics()
    {
        var (context, txn, fakeTransaction) = CreateOpenTransactionWithMetrics();
        using var _ = context;
        fakeTransaction.CommitException = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<pengdows.crud.exceptions.TransactionException>(async () => await txn.CommitAsync());

        // The public completion API already gets this right...
        Assert.False(txn.WasCommitted);
        Assert.False(txn.WasRolledBack);

        // ...but metrics currently do not: a failed commit is neither a real commit nor a real
        // rollback, so it must not be counted as either.
        Assert.Equal(0, context.Metrics.TransactionsCommitted);
        Assert.Equal(0, context.Metrics.TransactionsRolledBack);
    }
}
