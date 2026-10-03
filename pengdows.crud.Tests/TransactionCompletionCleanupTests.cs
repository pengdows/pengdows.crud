using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-065: completing a transaction ran its clean-up steps one after another in a finally block,
/// so a provider whose transaction Dispose threw skipped the rest: the connection was never closed
/// and returned, nor the single-connection gate or the metrics.
/// </summary>
public sealed class TransactionCompletionCleanupTests
{
    private static (DatabaseContext Context, fakeDbConnection Connection) ContextWhoseTransactionDisposeThrows()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;Database=d;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        }, factory);
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        connection.SetTransactionDisposeException(new InvalidOperationException("provider dispose failed"));
        factory.Connections.Add(connection);
        return (context, connection);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SyncCompletion_ThrowingTransactionDispose_StillReleasesTheConnection(bool commit)
    {
        var (context, connection) = ContextWhoseTransactionDisposeThrows();
        using var _ = context;

        var tx = context.BeginTransaction();
        Record.Exception(() =>
        {
            if (commit)
            {
                tx.Commit();
            }
            else
            {
                tx.Rollback();
            }
        });

        Assert.True(connection.DisposeCount > 0, "the transaction's connection was never released");
        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.True(commit ? tx.WasCommitted : tx.WasRolledBack);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncCompletion_ThrowingTransactionDispose_StillReleasesTheConnection(bool commit)
    {
        var (context, connection) = ContextWhoseTransactionDisposeThrows();
        await using var _ = context;

        var tx = await context.BeginTransactionAsync();
        await Record.ExceptionAsync(async () =>
        {
            if (commit)
            {
                await tx.CommitAsync();
            }
            else
            {
                await tx.RollbackAsync();
            }
        });

        Assert.True(connection.DisposeCount > 0, "the transaction's connection was never released");
        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.True(commit ? tx.WasCommitted : tx.WasRolledBack);
    }
}
