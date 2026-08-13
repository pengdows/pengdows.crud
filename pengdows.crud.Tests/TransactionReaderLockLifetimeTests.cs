using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A TransactionContext serializes access to its pinned connection via its own reusable
/// lock (<c>_userLock</c>) — the connection's own lock is deliberately NoOp, since the
/// transaction lock already provides exclusivity. That means the transaction lock must stay
/// held for as long as any operation is using the connection, including the full lifetime of
/// an open reader — otherwise a second operation on the same TransactionContext could reach
/// the pinned connection concurrently with an in-flight reader.
/// </summary>
public class TransactionReaderLockLifetimeTests
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

    private static SemaphoreSlim GetUserLock(ITransactionContext tx)
    {
        var field = typeof(TransactionContext).GetField("_userLock", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (SemaphoreSlim)field!.GetValue(tx)!;
    }

    [Fact]
    public async Task ExecuteReaderAsync_InTransaction_KeepsTransactionLockHeldUntilReaderDisposed()
    {
        using var tx = CreateContext().BeginTransaction();
        var userLock = GetUserLock(tx);

        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        // BUG today: ExecuteReaderAsyncInternal's finally block unconditionally disposes
        // the transaction's own lock once the method returns, regardless of whether a
        // TrackedReader holding an open cursor was handed back to the caller — only the
        // connection-level lock (NoOp, for a transaction's pinned connection) is transferred.
        Assert.Equal(0, userLock.CurrentCount); // should still be HELD while the reader is open

        await reader.DisposeAsync();
        Assert.Equal(1, userLock.CurrentCount); // released once the reader itself is disposed
    }

    [Fact]
    public async Task ExecuteReaderAsync_InTransaction_NestedOperationOnSameFlow_ThrowsImmediately_WhileReaderOpen()
    {
        using var tx = CreateContext().BeginTransaction();

        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        // A nested operation on the SAME transaction, from the SAME logical call flow that
        // opened the still-active reader (e.g. a write issued mid-stream while iterating
        // RetrieveStreamAsync), can never succeed — nothing will release the lock until this
        // very call returns. Failing fast with a clear exception is correct; blocking would
        // hang forever, since a reentrant lock would let the nested call reach the provider
        // while a reader is still open on the same connection, which most providers don't
        // support either.
        var secondLocker = tx.GetLock();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => secondLocker.LockAsync().AsTask());
        stopwatch.Stop();

        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, "Nested same-flow use must fail fast, not block.");

        await reader.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteReaderAsync_InTransaction_AnotherThread_AlsoThrowsImmediately_WhileReaderOpen()
    {
        using var tx = CreateContext().BeginTransaction();

        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        // A genuinely different logical caller (another thread sharing the same
        // TransactionContext) must also fail fast, not wait — a reader left open on the
        // connection means nobody can safely use it until the reader is disposed, regardless
        // of who's asking. Waiting would either hang forever (if the waiter is the same flow
        // that must dispose the reader) or eventually contend with the provider anyway (most
        // providers reject a second command while a reader is open on the same connection).
        var ex = await Task.Run(() =>
        {
            var secondLocker = tx.GetLock();
            return Assert.ThrowsAsync<InvalidOperationException>(
                () => secondLocker.TryLockAsync(TimeSpan.FromSeconds(30)).AsTask());
        });

        await reader.DisposeAsync();

        Assert.Contains("reader", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
