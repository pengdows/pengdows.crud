using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class TransactionCriticalPathTests
{
    [Fact]
    public void Commit_MarksTransactionAndPreventsSecondCompletion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);

        using var transaction = context.BeginTransaction();
        transaction.Commit();

        Assert.True(transaction.WasCommitted);
        Assert.False(transaction.WasRolledBack);

        var exception = Assert.Throws<InvalidOperationException>(transaction.Commit);
        Assert.Contains("already completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rollback_MarksTransactionAndBlocksCommit()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);

        using var transaction = context.BeginTransaction();
        transaction.Rollback();

        Assert.True(transaction.WasRolledBack);
        Assert.False(transaction.WasCommitted);

        var exception = Assert.Throws<InvalidOperationException>(transaction.Commit);
        Assert.Contains("already completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSqlContainer_AfterCompletion_ThrowsInvalidOperation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);

        using var transaction = context.BeginTransaction();
        transaction.Commit();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            transaction.CreateSqlContainer("SELECT 1"));

        Assert.Contains("transaction is completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetLock_AfterCompletion_ThrowsInvalidOperation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext("Data Source=:memory:", factory);

        using var transaction = context.BeginTransaction();
        transaction.Rollback();

        var exception = Assert.Throws<InvalidOperationException>(() => transaction.GetLock());
        Assert.Contains("Transaction already completed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SavepointOperations_OnSupportingDialect_DoNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var context = new DatabaseContext("Host=localhost;Database=test", factory);

        using var transaction = context.BeginTransaction();
        await transaction.SavepointAsync("sp_test");
        await transaction.RollbackToSavepointAsync("sp_test");
        transaction.Commit();

        Assert.True(transaction.WasCommitted);
    }
}