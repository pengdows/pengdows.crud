using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CORE-023: TransactionContext's completion path (Commit/Rollback/Dispose-triggered rollback)
/// and Savepoint operations previously coordinated only through the internal _completionLock,
/// never consulting _userLock/_reusableLocker — the same lock that ExecuteReaderAsync marks via
/// ReusableAsyncLocker.MarkHeldByActiveReader() while a reader is open on the transaction's
/// pinned connection. That meant Commit()/Rollback() (which dispose the transaction and close
/// the connection) and Savepoint commands could run concurrently with, or immediately after
/// opening, a still-active reader on the SAME connection — silently corrupting or racing against
/// live provider state instead of failing loudly.
///
/// The fix routes completion and savepoints through the same reader-aware lock ordinary commands
/// already use (see TransactionReaderLockLifetimeTests for that existing mechanism), so all of
/// these fail fast with a clear exception instead of proceeding while a reader is open.
/// </summary>
public class TransactionCompletionReaderGuardTests
{
    private static IDatabaseContext CreateContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.SingleWriter,
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}"
        };

        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task Commit_WhileReaderOpen_ThrowsInsteadOfDisposingConnectionUnderneathIt()
    {
        using var tx = CreateContext().BeginTransaction();
        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        var ex = Assert.Throws<InvalidOperationException>(() => tx.Commit());
        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The transaction must NOT be marked completed by the failed attempt — the caller can
        // dispose the reader and retry cleanly.
        Assert.False(tx.IsCompleted);

        await reader.DisposeAsync();
        tx.Commit();
        Assert.True(tx.IsCompleted);
    }

    [Fact]
    public async Task CommitAsync_WhileReaderOpen_ThrowsInsteadOfDisposingConnectionUnderneathIt()
    {
        var ctx = CreateContext();
        var tx = await ctx.BeginTransactionAsync();
        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.CommitAsync());
        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(tx.IsCompleted);

        await reader.DisposeAsync();
        await tx.CommitAsync();
        Assert.True(tx.IsCompleted);

        await ((IAsyncDisposable)ctx).DisposeAsync();
    }

    [Fact]
    public async Task Rollback_WhileReaderOpen_ThrowsInsteadOfDisposingConnectionUnderneathIt()
    {
        using var tx = CreateContext().BeginTransaction();
        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        var ex = Assert.Throws<InvalidOperationException>(() => tx.Rollback());
        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(tx.IsCompleted);

        await reader.DisposeAsync();
        tx.Rollback();
        Assert.True(tx.IsCompleted);
    }

    [Fact]
    public async Task RollbackAsync_WhileReaderOpen_ThrowsInsteadOfDisposingConnectionUnderneathIt()
    {
        var ctx = CreateContext();
        var tx = await ctx.BeginTransactionAsync();
        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.RollbackAsync());
        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(tx.IsCompleted);

        await reader.DisposeAsync();
        await tx.RollbackAsync();
        Assert.True(tx.IsCompleted);

        await ((IAsyncDisposable)ctx).DisposeAsync();
    }

    [Fact]
    public async Task Dispose_WhileReaderOpen_DoesNotDisposeConnectionOrTransactionUnderneathIt()
    {
        // Dispose()'s internal auto-rollback-if-not-completed path calls into the same
        // CompleteTransaction core as an explicit Rollback() — it must be guarded identically.
        // Dispose() itself must not throw (matching its documented best-effort contract), but it
        // must not tear down the connection/transaction while the reader still owns them either.
        using var tx = CreateContext().BeginTransaction();
        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();
        var fakeTx = (fakeDbTransaction)((TransactionContext)tx).Transaction;

        var disposeEx = Record.Exception(() => ((IDisposable)tx).Dispose());
        Assert.Null(disposeEx);
        Assert.Equal(0, fakeTx.DisposeCallCount);

        await reader.DisposeAsync();
    }

    [Fact]
    public async Task SavepointAsync_WhileReaderOpen_ThrowsInsteadOfRacingTheConnection()
    {
        using var tx = CreateContext().BeginTransaction();
        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await tx.SavepointAsync("sp1"));
        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);

        await reader.DisposeAsync();
    }

    [Fact]
    public async Task RollbackToSavepointAsync_WhileReaderOpen_ThrowsInsteadOfRacingTheConnection()
    {
        using var tx = CreateContext().BeginTransaction();
        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await tx.RollbackToSavepointAsync("sp1"));
        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);

        await reader.DisposeAsync();
    }
}
