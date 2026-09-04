using System.Diagnostics;
using Npgsql;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// Real-driver, real-network regression coverage for the sync-over-async pool-acquisition defect
/// fixed alongside this test (see pengdows.crud.Tests.AsyncPoolAcquisitionRegressionTests for the
/// FakeDb-level proof of the semaphore/ThreadPool mechanics). That coverage proves the mechanism
/// in isolation; these tests prove the fix holds against a real PostgreSQL server and the real
/// Npgsql driver -- the exact shape of the original defect, which was discovered via a benchmark
/// harness saturating a small pengdows.crud connection pool against real PostgreSQL, not FakeDb.
///
/// Two tests, two different rigor levels:
///
/// 1. <see cref="ExecuteReaderAsync_DoesNotBlockCallingThread_AgainstRealPostgres"/> is
///    deterministic and environment-independent: it measures whether the call to
///    ExecuteReaderAsync() itself (not awaiting the result) returns promptly when the pool is
///    saturated, exactly mirroring the FakeDb-level test's proven methodology. This does not
///    depend on CLR ThreadPool sizing/core count/system load and reliably distinguishes the
///    pre-fix defect from the fix on any machine.
///
/// 2. <see cref="ExecuteReaderAsync_UnderRealConcurrentLoad_CompletesPromptly"/> is a bulk
///    real-load sanity check (many concurrent operations exceeding the pool size). NOTE: this
///    was empirically found NOT to reliably distinguish pre-fix from post-fix at modest
///    concurrency (20 ops / pool of 2) on a multi-core CI-class box -- the CLR's ThreadPool
///    thread-injection is fast enough on modern .NET that 18 blocked waiters alone didn't trigger
///    a dramatic stall in every run, even against the unfixed code (measured 526ms, well under a
///    naive threshold, on the unfixed build). It is kept as a real-load regression guard with a
///    generous threshold, but test (1) above is the reliable proof of the actual fix.
/// </summary>
[Collection("IntegrationTests")]
public class AsyncPoolAcquisitionRealDriverTests : DatabaseTestBase
{
    public AsyncPoolAcquisitionRealDriverTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return new[] { SupportedDatabase.PostgreSql };
    }

    private async Task<DatabaseContext> CreateSmallPoolContextAsync(int poolSize, TimeSpan acquireTimeout)
    {
        var connectionString = GetRawConnectionString(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ProviderName = "Npgsql",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = poolSize,
            MaxConcurrentWrites = poolSize,
            PoolAcquireTimeout = acquireTimeout,
        };

        var context = new DatabaseContext(config, NpgsqlFactory.Instance);

        // Warm up: open+close one connection so the pool/driver/dialect-detection cost doesn't
        // pollute the timing-sensitive assertions below.
        var warmup = context.CreateSqlContainer("SELECT 1");
        await using (var warmupReader = await warmup.ExecuteReaderAsync())
        {
            await warmupReader.ReadAsync();
        }

        return context;
    }

    [SkippableFact]
    public async Task ExecuteReaderAsync_DoesNotBlockCallingThread_AgainstRealPostgres()
    {
        await using var context = await CreateSmallPoolContextAsync(
            poolSize: 1, acquireTimeout: TimeSpan.FromSeconds(3));

        // Acquire the pool's only real connection slot and hold it open.
        var sc1 = context.CreateSqlContainer("SELECT 1");
        var reader1 = await sc1.ExecuteReaderAsync();

        // Act: a second read must wait for the slot (real PostgreSQL, real Npgsql). The call
        // itself -- not awaiting its result -- must return promptly, since the pool wait should
        // only be reached after suspending on a genuine await point (PoolGovernor.AcquireAsync),
        // not by blocking this thread synchronously inside PoolGovernor.Acquire() before any
        // await is reached. This is the same methodology as
        // pengdows.crud.Tests.AsyncPoolAcquisitionRegressionTests, now against a real server.
        var sc2 = context.CreateSqlContainer("SELECT 1");
        var sw = Stopwatch.StartNew();
        var pending = sc2.ExecuteReaderAsync().AsTask();
        sw.Stop();

        Output.WriteLine(
            $"ExecuteReaderAsync() call against real PostgreSQL returned in {sw.ElapsedMilliseconds}ms " +
            "while the pool was saturated (threshold: 250ms).");

        Assert.True(sw.ElapsedMilliseconds < 250,
            $"ExecuteReaderAsync() blocked the calling thread for {sw.ElapsedMilliseconds}ms " +
            "before returning a pending task while waiting for a real-PostgreSQL pool slot -- " +
            "sync-over-async regression (PoolGovernor.Acquire() reached instead of AcquireAsync()).");

        reader1.Dispose();
        using var reader2 = await pending;
        Assert.NotNull(reader2);
    }

    [SkippableFact]
    public async Task ExecuteReaderAsync_UnderRealConcurrentLoad_CompletesPromptly()
    {
        const int poolSize = 2;
        const int concurrentOperations = 20;
        const int holdMs = 50;

        // Theoretical minimum with a perfectly fair, zero-overhead 2-slot pool:
        // ceil(20/2) * 50ms = 500ms. Generous threshold above that -- see class remarks for why
        // this specific bulk shape did not reliably distinguish pre-fix from post-fix on this
        // box; it remains a real-load regression guard, not the primary proof.
        var maxAcceptableElapsed = TimeSpan.FromSeconds(3);

        await using var context = await CreateSmallPoolContextAsync(
            poolSize, acquireTimeout: TimeSpan.FromSeconds(30));

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrentOperations).Select(async _ =>
        {
            var sc = context.CreateSqlContainer("SELECT 1");
            await using var reader = await sc.ExecuteReaderAsync();
            await reader.ReadAsync();
            await Task.Delay(holdMs);
        }).ToArray();

        await Task.WhenAll(tasks);
        sw.Stop();

        Output.WriteLine(
            $"{concurrentOperations} concurrent reads against a {poolSize}-slot real PostgreSQL " +
            $"pool completed in {sw.ElapsedMilliseconds}ms (threshold: {maxAcceptableElapsed.TotalMilliseconds}ms).");

        Assert.True(sw.Elapsed < maxAcceptableElapsed,
            $"{concurrentOperations} concurrent reads against a {poolSize}-slot pool took " +
            $"{sw.ElapsedMilliseconds}ms, exceeding the {maxAcceptableElapsed.TotalMilliseconds}ms threshold.");
    }
}
