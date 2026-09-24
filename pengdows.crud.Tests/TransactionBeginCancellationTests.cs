using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// OperationCanceledException is never wrapped. When the cancellation lands in the provider's
/// BeginTransaction, TransactionContext wrapped it in TransactionException ("Failed to begin
/// transaction…"), so callers catching cancellation missed it — seen intermittently as
/// RetryContextTransactionalExecutionTests.StartAsync_CancellationDuringBackoffPropagatesUnwrapped
/// on 3.0, depending on exactly when the token fired.
/// </summary>
public class TransactionBeginCancellationTests
{
    private static (DatabaseContext Context, fakeDbConnection Connection) CreateContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
                DbMode = DbMode.SingleConnection
            },
            factory, NullLoggerFactory.Instance);
        return (context, connection);
    }

    [Fact]
    public async Task BeginTransactionAsync_CancelledDuringBegin_PropagatesUnwrapped()
    {
        var (context, connection) = CreateContext();
        await using var _ = context;
        connection.SetCustomFailureException(new OperationCanceledException("cancelled during begin"));
        connection.SetFailOnBeginTransaction();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await context.BeginTransactionAsync(IsolationLevel.ReadCommitted));

        // The gate and connection were released: a later transaction still begins.
        connection.SetFailOnBeginTransaction(false);
        await using var tx = await context.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await tx.RollbackAsync();
    }

    [Fact]
    public void BeginTransaction_CancelledDuringBegin_PropagatesUnwrapped()
    {
        var (context, connection) = CreateContext();
        using var _ = context;
        connection.SetCustomFailureException(new OperationCanceledException("cancelled during begin"));
        connection.SetFailOnBeginTransaction();

        Assert.Throws<OperationCanceledException>(() => context.BeginTransaction(IsolationLevel.ReadCommitted));

        connection.SetFailOnBeginTransaction(false);
        using var tx = context.BeginTransaction(IsolationLevel.ReadCommitted);
        tx.Rollback();
    }
}
