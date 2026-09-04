using System;
using System.Diagnostics;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Regression coverage for a real sync-over-async defect found via the pengdows/wiki benchmark:
/// under concurrency exceeding a PoolGovernor-managed pool's slot count, throughput collapsed far
/// more catastrophically than pool queueing alone predicts. Root cause: the connection-acquisition
/// hot path (SqlContainer.ExecuteReaderAsyncInternal/ExecuteNonQueryAsync -> ... ->
/// DatabaseContext.AcquireSlot -> PoolGovernor.Acquire()) is fully synchronous even though every
/// caller in this chain is an async method with no prior genuine await (NoOpAsyncLocker awaits
/// complete synchronously). That means an async call that has to wait for a pool slot blocks the
/// CALLING thread for the full wait, instead of yielding via PoolGovernor.AcquireAsync (which
/// already exists and is correct) -- CLR ThreadPool starvation under load, confirmed empirically:
/// bumping ThreadPool.SetMinThreads alone (no other change) took the same 10-slot pool at
/// concurrency=50 from 8.1 req/s / 98 timeouts to 5,293 req/s / 0 timeouts.
/// </summary>
public sealed class AsyncPoolAcquisitionRegressionTests
{
    [Fact(Timeout = 10000)]
    public async Task ExecuteReaderAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 1,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(3),
        };
        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));

        // Acquire the pool's only slot and hold it open.
        using var sc1 = context.CreateSqlContainer("SELECT 1");
        var reader1 = await sc1.ExecuteReaderAsync();

        // Act: a second read must wait for the slot. The call itself -- not awaiting its result --
        // must return promptly (a pending ValueTask), since the pool wait should be reached only
        // after suspending on a genuine await point (PoolGovernor.AcquireAsync), not by blocking
        // this thread synchronously inside PoolGovernor.Acquire() before any await is reached.
        using var sc2 = context.CreateSqlContainer("SELECT 1");
        var sw = Stopwatch.StartNew();
        var pending = sc2.ExecuteReaderAsync().AsTask();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 250,
            $"ExecuteReaderAsync() blocked the calling thread for {sw.ElapsedMilliseconds}ms " +
            "before returning a pending task while waiting for a pool slot -- sync-over-async " +
            "regression (PoolGovernor.Acquire() reached instead of AcquireAsync()).");

        // Cleanup: release the held slot so the pending acquire can complete.
        reader1.Dispose();
        using var reader2 = await pending;
        Assert.NotNull(reader2);
    }

    [Fact(Timeout = 10000)]
    public async Task ExecuteNonQueryAsync_DoesNotBlockCallingThread_WhenPoolSlotUnavailable()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 1,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(3),
        };
        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));

        using var sc1 = context.CreateSqlContainer("SELECT 1");
        var reader1 = await sc1.ExecuteReaderAsync();

        using var sc2 = context.CreateSqlContainer("SELECT 1");
        var sw = Stopwatch.StartNew();
        var pending = sc2.ExecuteNonQueryAsync(ExecutionType.Read).AsTask();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 250,
            $"ExecuteNonQueryAsync() blocked the calling thread for {sw.ElapsedMilliseconds}ms " +
            "before returning a pending task while waiting for a pool slot -- sync-over-async regression.");

        reader1.Dispose();
        await pending;
    }
}
