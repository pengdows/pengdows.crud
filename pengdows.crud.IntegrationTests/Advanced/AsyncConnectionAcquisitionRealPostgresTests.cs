using System.Diagnostics;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.@internal;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// Real-driver, real-network regression coverage for the sync-over-async connection-acquisition
/// defect found via the pengdows.wiki benchmark (see AsyncConnectionAcquisitionTests.cs in
/// pengdows.crud.Tests for the FakeDb-based version, which proves the semaphore/ThreadPool
/// mechanics deterministically but has no real network I/O or real driver behavior). This test
/// proves the same thing against the real Npgsql driver and a real PostgreSQL server: with the
/// pool's only slot held by an in-flight real query, a second concurrent ExecuteReaderAsync must
/// return an incomplete awaitable to the caller almost immediately, not block the calling thread
/// inside PoolGovernor's synchronous Acquire() for the length of the wait.
///
/// A statistical variant (100 concurrent real reads against a 10-slot pool, asserting zero
/// PoolSaturatedException timeouts -- the exact scenario that measured 98/300 real timeouts via
/// the pengdows.wiki benchmark's standalone console harness) was tried first and discarded: inside
/// the xUnit/VSTest test host process the CLR ThreadPool already carries extra warm capacity from
/// xUnit's own test-dispatch infrastructure, so that scenario didn't reproduce the starvation
/// reliably in this host even against the pre-fix code -- a real limitation of reproducing a
/// ThreadPool-starvation timing effect inside a test runner process, not evidence the defect only
/// exists in a synthetic benchmark. The deterministic single-waiter test below has no such
/// dependency on ambient ThreadPool state.
/// </summary>
[Collection("IntegrationTests")]
public class AsyncConnectionAcquisitionRealPostgresTests : DatabaseTestBase
{
    public AsyncConnectionAcquisitionRealPostgresTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    [SkippableFact]
    public async Task ExecuteReaderAsync_RealPostgres_DoesNotBlockCallingThread_WhenPoolSlotHeldByRealQuery()
    {
        // Reuse the fixture's already-running PostgreSQL Testcontainer's real connection string
        // (via the same internal accessor SqlContainer itself uses -- IDatabaseContext.ConnectionString
        // is deliberately redacted for display, see SecurityRegressionTests), but build our OWN
        // DatabaseContext with a deliberately single-slot pool so a genuine admission wait occurs.
        var fixtureContext = await Fixture.CreateAdditionalContextAsync(SupportedDatabase.PostgreSql);
        var rawConnectionString = InternalConnectionStringAccess.GetRawConnectionString(fixtureContext);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = rawConnectionString,
            ProviderName = "Npgsql",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(10),
        };

        await using var context = new DatabaseContext(config, Npgsql.NpgsqlFactory.Instance);

        // Hold the single slot open with a real, still-running query against real PostgreSQL.
        using var sc1 = context.CreateSqlContainer("SELECT pg_sleep(2)");
        var holderTask = sc1.ExecuteScalarOrNullAsync<int>().AsTask();

        // Give the first query a moment to actually acquire the slot and start executing on the
        // server before contending for the (only) slot with the second.
        await Task.Delay(200);

        using var sc2 = context.CreateSqlContainer("SELECT 1");

        // Under the pre-fix synchronous PoolGovernor.Acquire() (SemaphoreSlim.Wait), this call
        // blocks THIS thread for up to ~1.8 more seconds (until sc1's pg_sleep(2) releases the
        // slot) before it can even return a Task to await. Under the fix, PoolGovernor.AcquireAsync
        // (SemaphoreSlim.WaitAsync) yields immediately, so this line returns an incomplete Task
        // well under 200ms regardless of how long the real slot-holder still has to run.
        var sw = Stopwatch.StartNew();
        var secondTask = sc2.ExecuteScalarOrNullAsync<int>().AsTask();
        var elapsedToReturn = sw.Elapsed;

        Assert.True(elapsedToReturn < TimeSpan.FromMilliseconds(500),
            $"ExecuteScalarOrNullAsync took {elapsedToReturn.TotalMilliseconds:F0}ms to return control " +
            "to the caller while the real PostgreSQL pool's only slot was held by an in-flight real " +
            "query -- it should return an incomplete Task almost immediately instead of blocking the " +
            "calling thread inside PoolGovernor's synchronous Acquire().");

        await holderTask;
        var second = await secondTask;
        Assert.Equal(1, second);
    }
}
