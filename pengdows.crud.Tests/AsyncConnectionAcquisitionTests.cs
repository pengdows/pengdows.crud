using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Regression coverage for a sync-over-async defect found via the pengdows/wiki benchmark:
/// under load exceeding a PoolGovernor-managed pool's slot count, the async execution paths
/// (ExecuteReaderAsync, ExecuteNonQueryAsync, TransactionContext.CreateAsync) reached the
/// SYNCHRONOUS, blocking PoolGovernor.Acquire() (SemaphoreSlim.Wait) instead of the existing
/// AcquireAsync() (SemaphoreSlim.WaitAsync). Every waiter therefore blocked a real CLR
/// ThreadPool thread instead of yielding, which under concurrent load starves the ThreadPool
/// and produces a catastrophic throughput collapse far beyond what the pool's own admission
/// control would otherwise cause (confirmed empirically: the same 10-slot pool at
/// concurrency=50 went from 8.1 req/s / 98 timeouts to 5,293 req/s / 0 timeouts purely by
/// bumping ThreadPool.SetMinThreads before the run -- i.e. a CLR starvation artifact, not a
/// PoolGovernor design defect).
///
/// These tests prove the calling thread is never blocked waiting for a pool slot: with a
/// single-slot pool already held by one in-flight operation, starting a second operation
/// must return control to the caller immediately (a pending awaitable), not hang the calling
/// thread until the first operation releases its slot. Under the pre-fix synchronous
/// acquisition path, the very act of *calling* the second operation (before ever awaiting it)
/// blocks the test's own thread forever, since nothing else can run to release the first
/// slot -- a deterministic single-threaded deadlock caught by the Timeout below.
/// </summary>
public class AsyncConnectionAcquisitionTests
{
    [Fact(Timeout = 5000)]
    public async Task ExecuteReaderAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentReads = 1,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn1 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn1.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn1);

        using var sc1 = ctx.CreateSqlContainer("SELECT id FROM test");
        var reader1 = await sc1.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default);

        var snapshotDuring = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(1, snapshotDuring.InUse);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn2.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 2 } });
        factory.Connections.Add(conn2);

        using var sc2 = ctx.CreateSqlContainer("SELECT id FROM test");

        // The line below must NOT block this thread. Under the pre-fix bug it blocks
        // synchronously inside PoolGovernor.Acquire() before returning any awaitable at all,
        // so reader1.DisposeAsync() below is never reached -- a deterministic deadlock, only
        // escaped by the [Fact(Timeout = 5000)] failing the test instead of hanging forever.
        var pendingTask = sc2.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default).AsTask();

        await reader1.DisposeAsync();

        using var reader2 = await pendingTask;
        Assert.NotNull(reader2);
    }

    [Fact(Timeout = 5000)]
    public async Task ExecuteNonQueryAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentWrites = 1,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Hold the only writer slot open via a live reader on a write-intent execution
        // (CommandBehavior without CloseConnection keeps the slot held until disposed), then
        // attempt a second write that must not block this thread while the slot is held.
        var conn1 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn1.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn1);

        using var sc1 = ctx.CreateSqlContainer("SELECT id FROM test");
        var reader1 = await sc1.ExecuteReaderAsync(ExecutionType.Write, CommandType.Text, default);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn2.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn2);

        using var sc2 = ctx.CreateSqlContainer("UPDATE test SET id = 2");

        // Must NOT block this thread. Under the pre-fix bug, ExecuteNonQueryAsync's call site
        // (distinct from the reader path above) also reaches the synchronous PoolGovernor.Acquire()
        // before any await, so this line would hang until reader1 is disposed -- which never
        // happens, since the very next line is what disposes it.
        var pendingTask = sc2.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, default).AsTask();

        await reader1.DisposeAsync();

        var affected = await pendingTask;
        Assert.Equal(1, affected);
    }

    [Fact(Timeout = 5000)]
    public async Task TransactionContext_CreateAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentWrites = 1,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn1 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        factory.Connections.Add(conn1);

        // Hold the only writer slot open via a live transaction, then attempt a second
        // BeginTransactionAsync that must not block this thread while the slot is held.
        var txn1 = await ctx.BeginTransactionAsync(executionType: ExecutionType.Write);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        factory.Connections.Add(conn2);

        var pendingTask = ctx.BeginTransactionAsync(executionType: ExecutionType.Write).AsTask();

        await txn1.DisposeAsync();

        var txn2 = await pendingTask;
        await txn2.DisposeAsync();
    }
}
