using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

public class TransactionExceptionWiringTests
{
    private static DatabaseContext CreateContext(fakeDbFactory factory)
        => new($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}", factory);

    // -------------------------------------------------------------------------
    // Begin failures
    // -------------------------------------------------------------------------

    [Fact]
    public void BeginTransaction_WhenConnectionFails_Throws_TransactionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailOnTransaction);
        using var ctx = CreateContext(factory);

        var ex = Assert.Throws<TransactionException>(() => ctx.BeginTransaction());

        Assert.NotNull(ex.InnerException);
        Assert.Contains("begin transaction", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BeginTransactionAsync_WhenConnectionFails_Throws_TransactionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite,
            ConnectionFailureMode.FailOnTransaction);
        await using var ctx = CreateContext(factory);

        var ex = await Assert.ThrowsAsync<TransactionException>(
            async () => await ctx.BeginTransactionAsync());

        Assert.NotNull(ex.InnerException);
        Assert.Contains("begin transaction", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Commit failures
    // -------------------------------------------------------------------------

    [Fact]
    public void Commit_WhenProviderFails_Throws_TransactionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetGlobalTransactionCommitException(
            new InvalidOperationException("simulated commit failure"));
        using var ctx = CreateContext(factory);
        using var tx = ctx.BeginTransaction();

        var ex = Assert.Throws<TransactionException>(() => tx.Commit());

        Assert.NotNull(ex.InnerException);
        Assert.Contains("commit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommitAsync_WhenProviderFails_Throws_TransactionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetGlobalTransactionCommitException(
            new InvalidOperationException("simulated commit failure"));
        await using var ctx = CreateContext(factory);
        await using var tx = await ctx.BeginTransactionAsync();

        var ex = await Assert.ThrowsAsync<TransactionException>(
            async () => await tx.CommitAsync());

        Assert.NotNull(ex.InnerException);
        Assert.Contains("commit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Rollback failures
    // -------------------------------------------------------------------------

    [Fact]
    public void Rollback_WhenProviderFails_Throws_TransactionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetGlobalTransactionRollbackException(
            new InvalidOperationException("simulated rollback failure"));
        using var ctx = CreateContext(factory);
        using var tx = ctx.BeginTransaction();

        var ex = Assert.Throws<TransactionException>(() => tx.Rollback());

        Assert.NotNull(ex.InnerException);
        Assert.Contains("rollback", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackAsync_WhenProviderFails_Throws_TransactionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetGlobalTransactionRollbackException(
            new InvalidOperationException("simulated rollback failure"));
        await using var ctx = CreateContext(factory);
        await using var tx = await ctx.BeginTransactionAsync();

        var ex = await Assert.ThrowsAsync<TransactionException>(
            async () => await tx.RollbackAsync());

        Assert.NotNull(ex.InnerException);
        Assert.Contains("rollback", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // State after failure
    // -------------------------------------------------------------------------

    [Fact]
    public void AfterCommitFailure_TransactionContext_IsCompleted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetGlobalTransactionCommitException(
            new InvalidOperationException("simulated commit failure"));
        using var ctx = CreateContext(factory);
        using var tx = ctx.BeginTransaction();

        Assert.Throws<TransactionException>(() => tx.Commit());

        Assert.True(tx.IsCompleted);
    }
}
