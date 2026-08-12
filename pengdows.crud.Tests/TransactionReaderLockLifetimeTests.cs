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
    public async Task ExecuteReaderAsync_InTransaction_BlocksConcurrentOperation_WhileReaderOpen()
    {
        using var tx = CreateContext().BeginTransaction();

        var container = tx.CreateSqlContainer("SELECT 1");
        var reader = await container.ExecuteReaderAsync();

        // A second operation reaching for the SAME TransactionContext's lock while the first
        // reader is still open must not be able to proceed concurrently.
        var secondLocker = tx.GetLock();
        var acquiredConcurrently = await secondLocker.TryLockAsync(TimeSpan.FromMilliseconds(10));

        if (acquiredConcurrently)
        {
            await secondLocker.DisposeAsync();
        }

        await reader.DisposeAsync();

        Assert.False(acquiredConcurrently); // BUG today: this is true — lock was already released
    }
}
