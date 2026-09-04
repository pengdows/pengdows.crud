using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Regression coverage for a real sync-over-async defect found via the pengdows.wiki benchmark:
/// under load exceeding a PoolGovernor-managed pool's slot count, throughput collapsed far more
/// than pool queueing alone predicts (e.g. 63.8 req/s -> 8.1 req/s with 98/300 requests timing
/// out at concurrency=50 against a 10-slot pool). Root cause: the connection-acquisition hot
/// path (SqlContainer.ExecuteReaderAsyncInternal/ExecuteNonQueryAsync -> ... -> DatabaseContext.
/// AcquireSlot -> PoolGovernor.Acquire) was fully synchronous (SemaphoreSlim.Wait) even though
/// it is reached exclusively from async methods, so every waiter blocked a real CLR ThreadPool
/// thread instead of yielding via WaitAsync. Confirmed empirically: adding
/// ThreadPool.SetMinThreads(200,200) to the benchmark harness -- nothing else -- took the same
/// 10-slot pool at the same concurrency=50 from 8.1 req/s/98 timeouts to 5,293 req/s/0 timeouts.
/// </summary>
public class AsyncConnectionAcquisitionTests
{
    private static DatabaseContext CreateContext(fakeDbFactory factory, int maxConcurrentReads = 1)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString =
                "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentReads = maxConcurrentReads,
            DbMode = DbMode.Standard,
            EnableMetrics = true,
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
        };

        return new DatabaseContext(config, factory, NullLoggerFactory.Instance);
    }

    private static fakeDbConnection NewReaderConnection(int id)
    {
        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = id } });
        return conn;
    }

    [Fact(Timeout = 5000)]
    public async Task ExecuteReaderAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var ctx = CreateContext(factory);

        var conn1 = NewReaderConnection(1);
        factory.Connections.Add(conn1);
        using var sc1 = ctx.CreateSqlContainer("SELECT * FROM test");

        var reader1 = await sc1.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, CancellationToken.None);

        var snapshotDuring = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(1, snapshotDuring.InUse);

        var conn2 = NewReaderConnection(2);
        factory.Connections.Add(conn2);
        using var sc2 = ctx.CreateSqlContainer("SELECT * FROM test");

        // Under the bug, the very next statement blocks THIS thread synchronously inside
        // PoolGovernor.Acquire()'s SemaphoreSlim.Wait for up to PoolAcquireTimeout (2s here) --
        // the only slot is held by reader1, and reader1 can never be released because the code
        // that disposes it (below) never runs until this statement returns. Under the fix, this
        // returns an incomplete awaitable almost immediately since PoolGovernor.AcquireAsync's
        // WaitAsync yields instead of blocking.
        var sw = Stopwatch.StartNew();
        var pendingTask = sc2.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, CancellationToken.None)
            .AsTask();
        var elapsedToReturn = sw.Elapsed;

        Assert.True(elapsedToReturn < TimeSpan.FromMilliseconds(500),
            $"ExecuteReaderAsync took {elapsedToReturn.TotalMilliseconds:F0}ms to return control to the caller " +
            "while the pool was saturated -- it should return a pending awaitable immediately instead of " +
            "blocking the calling thread inside PoolGovernor's synchronous Acquire().");

        await reader1.DisposeAsync();

        using var reader2 = await pendingTask;
        Assert.NotNull(reader2);

        var snapshotAfter = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(1, snapshotAfter.InUse); // reader2 now holds the single slot
    }

    [Fact(Timeout = 5000)]
    public async Task ExecuteNonQueryAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString =
                "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentWrites = 1,
            DbMode = DbMode.Standard,
            EnableMetrics = true,
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
        };
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn1 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn1.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn1);
        using var sc1 = ctx.CreateSqlContainer("SELECT * FROM test");

        // Hold the single writer slot open via a reader (same "occupies the slot until disposed"
        // mechanism CancellationLeakTests already relies on), issued with ExecutionType.Write.
        var reader1 = await sc1.ExecuteReaderAsync(ExecutionType.Write, CommandType.Text, CancellationToken.None);

        var snapshotDuring = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(1, snapshotDuring.InUse);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn2.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn2);
        using var sc2 = ctx.CreateSqlContainer("UPDATE test SET id = 2");

        var sw = Stopwatch.StartNew();
        var pendingTask = sc2.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, CancellationToken.None)
            .AsTask();
        var elapsedToReturn = sw.Elapsed;

        Assert.True(elapsedToReturn < TimeSpan.FromMilliseconds(500),
            $"ExecuteNonQueryAsync took {elapsedToReturn.TotalMilliseconds:F0}ms to return control to the " +
            "caller while the pool was saturated -- it should return a pending awaitable immediately instead " +
            "of blocking the calling thread inside PoolGovernor's synchronous Acquire().");

        await reader1.DisposeAsync();

        var affected = await pendingTask;
        Assert.Equal(1, affected);
    }

    [Fact(Timeout = 5000)]
    public async Task BeginTransactionAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var ctx = CreateContext(factory);

        var conn1 = NewReaderConnection(1);
        factory.Connections.Add(conn1);
        using var sc1 = ctx.CreateSqlContainer("SELECT * FROM test");
        var reader1 = await sc1.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, CancellationToken.None);

        Assert.Equal(1, ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        factory.Connections.Add(conn2);

        var sw = Stopwatch.StartNew();
        var pendingTask = ctx.BeginTransactionAsync(executionType: ExecutionType.Read).AsTask();
        var elapsedToReturn = sw.Elapsed;

        Assert.True(elapsedToReturn < TimeSpan.FromMilliseconds(500),
            $"BeginTransactionAsync took {elapsedToReturn.TotalMilliseconds:F0}ms to return control to the " +
            "caller while the pool was saturated -- TransactionContext.CreateAsync must await the async " +
            "connection-acquisition path, not the sync GetConnection, under pool contention.");

        await reader1.DisposeAsync();

        using var txn = await pendingTask;
        Assert.NotNull(txn);
    }
}
