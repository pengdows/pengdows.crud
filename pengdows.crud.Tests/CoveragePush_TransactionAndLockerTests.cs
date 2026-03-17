using System;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class CoveragePush_TransactionAndLockerTests
{
    private static readonly MethodInfo ResolveCreationParametersMethod =
        typeof(TransactionContext).GetMethod(
            "ResolveCreationParameters",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExecuteSessionNonQueryMethod =
        typeof(TransactionContext).GetMethod(
            "ExecuteSessionNonQuery",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ExecuteSessionNonQueryAsyncMethod =
        typeof(TransactionContext).GetMethod(
            "ExecuteSessionNonQueryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public void RealAsyncLocker_SyncLockTimeout_ThrowsModeContentionException()
    {
        var stats = new ModeContentionStats();
        var semaphore = new SemaphoreSlim(1, 1);
        using var holder = new RealAsyncLocker(semaphore, stats, DbMode.SingleWriter, TimeSpan.FromMilliseconds(20));
        holder.Lock();

        using var waiter = new RealAsyncLocker(semaphore, stats, DbMode.SingleWriter, TimeSpan.FromMilliseconds(20));
        Assert.Throws<ModeContentionException>(() => waiter.Lock());
    }

    [Fact]
    public async Task RealAsyncLocker_TryLockAsync_ContendedThenReleased_ReturnsTrue()
    {
        var stats = new ModeContentionStats();
        var semaphore = new SemaphoreSlim(1, 1);
        await using var holder = new RealAsyncLocker(semaphore, stats, DbMode.SingleWriter, TimeSpan.FromSeconds(2));
        await holder.LockAsync();

        await using var waiter = new RealAsyncLocker(semaphore, stats, DbMode.SingleWriter, TimeSpan.FromSeconds(2));
        var acquisitionTask = waiter.TryLockAsync(TimeSpan.FromSeconds(1)).AsTask();

        var spinDeadline = DateTime.UtcNow.AddMilliseconds(500);
        while (stats.GetSnapshot().CurrentWaiters == 0 && DateTime.UtcNow < spinDeadline)
        {
            await Task.Delay(1);
        }
        Assert.True(stats.GetSnapshot().CurrentWaiters > 0);

        await holder.DisposeAsync();

        var acquired = await acquisitionTask;
        Assert.True(acquired);
    }

    [Fact]
    public async Task RealAsyncLocker_PreCanceledToken_ReturnsCanceledForLockAndTryLock()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var locker = new RealAsyncLocker(new SemaphoreSlim(1, 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locker.LockAsync(cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            locker.TryLockAsync(TimeSpan.FromMilliseconds(1), cts.Token).AsTask());
    }

    [Fact]
    public void TransactionContext_ResolveCreationParameters_RequiresInternalConnectionProvider()
    {
        var context = new Mock<IDatabaseContext>();
        context.SetupGet(c => c.IsReadOnlyConnection).Returns(false);
        context.SetupGet(c => c.Product).Returns(SupportedDatabase.Sqlite);

        var ex = Assert.Throws<TargetInvocationException>(() =>
            ResolveCreationParametersMethod.Invoke(null, new object?[]
            {
                context.Object,
                IsolationLevel.ReadCommitted,
                null
            }));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public async Task TransactionContext_ExecuteSessionNonQuery_WhitespaceIsNoOp()
    {
        using var context = new DatabaseContext(
            "Data Source=tx-branch;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        using var tx = (TransactionContext)context.BeginTransaction();

        ExecuteSessionNonQueryMethod.Invoke(tx, new object[] { "   " });

        var task = Assert.IsAssignableFrom<Task>(ExecuteSessionNonQueryAsyncMethod.Invoke(tx, new object[] { "   " }));
        await task;

        tx.Rollback();
    }

    [Fact]
    public void TransactionContext_PropertyForwarders_ExposeProcWrappingAndMetricsAccessors()
    {
        using var context = new DatabaseContext(
            "Data Source=tx-props;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        using var tx = (TransactionContext)context.BeginTransaction();

        Assert.Equal(context.ProcWrappingStyle, tx.ProcWrappingStyle);

        var accessor = (IMetricsCollectorAccessor)tx;
        _ = accessor.MetricsCollector;
        _ = accessor.ReadMetricsCollector;
        _ = accessor.WriteMetricsCollector;

        tx.Rollback();
    }
}
