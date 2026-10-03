using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-056: the synchronous Commit()/Rollback() ran CommitAsync().GetAwaiter().GetResult(), which
/// reaches the provider's real async commit (Npgsql, SqlClient, ...): sync-over-async on every sync
/// commit. The sync API uses the provider's sync calls.
/// </summary>
public sealed class TransactionSyncCompletionTests
{
    private static (DatabaseContext Context, TransactionContext Transaction, fakeDbTransaction Fake) Begin()
    {
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;Database=d;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard
        }, new fakeDbFactory(SupportedDatabase.PostgreSql));
        var transaction = (TransactionContext)context.BeginTransaction();
        return (context, transaction, (fakeDbTransaction)transaction.Transaction);
    }

    [Fact]
    public void Commit_UsesTheProvidersSyncCommit()
    {
        var (context, transaction, fake) = Begin();
        using var _ = context;

        transaction.Commit();

        Assert.Equal(1, fake.CommitCallCount);
        Assert.Equal(0, fake.CommitAsyncCallCount);
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public void Rollback_UsesTheProvidersSyncRollback()
    {
        var (context, transaction, fake) = Begin();
        using var _ = context;

        transaction.Rollback();

        Assert.Equal(1, fake.RollbackCallCount);
        Assert.Equal(0, fake.RollbackAsyncCallCount);
        Assert.True(transaction.WasRolledBack);
    }
}
