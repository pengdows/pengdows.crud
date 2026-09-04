using System.Data;
using System.Diagnostics;
using Npgsql;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// Real-driver, real-concurrency regression coverage for the sync-over-async connection
/// acquisition defect fixed in DatabaseContext.ConnectionLifecycle.cs / SqlContainer.cs /
/// TransactionContext.cs (see pengdows.crud.Tests.AsyncConnectionAcquisitionTests for the
/// FakeDb-based unit coverage of the same defect).
///
/// FakeDb has no real network I/O or real driver behavior, so it can only prove the
/// semaphore/ThreadPool mechanics in isolation -- it cannot prove the fix holds against a real
/// ADO.NET driver (Npgsql) issuing real queries over a real socket to a real PostgreSQL server
/// under genuine concurrent load, which is exactly how the original defect was discovered (a
/// benchmark harness saturating a small pengdows.crud connection pool against real Postgres).
/// This test reproduces that shape directly against a Testcontainers-provisioned PostgreSQL.
///
/// Mechanism: with MaxConcurrentReads = 2 and 20 concurrent real `SELECT pg_sleep(...)` queries
/// dispatched via Parallel.ForEachAsync (mirroring how the original benchmark harness dispatched
/// concurrent work), the only way to complete in time proportional to
/// ceil(20/2) * queryDuration is for the 18 queries that don't get an immediate pool slot to
/// await it asynchronously. Under the pre-fix synchronous PoolGovernor.Acquire() call reached
/// from the async execution path, each of those 18 waits blocks a real thread instead -- under
/// Parallel.ForEachAsync's real thread-pool-backed dispatch, this is the same CLR ThreadPool
/// starvation mechanism that produced the catastrophic scaling-curve collapse the original
/// benchmark found (documented in BENCHMARK_SPEC.md / the wiki benchmark's sweep results), and
/// takes dramatically longer than the pipelined ideal.
/// </summary>
[Collection("IntegrationTests")]
public class AsyncConnectionAcquisitionIntegrationTests : DatabaseTestBase
{
    private const int PoolSize = 2;
    private const int ConcurrentOperations = 20;
    private const double QueryDelaySeconds = 0.3;

    // Ideal (pipelined, non-blocking) wall clock: ceil(20/2) * 0.3s = 3.0s. Generous upper bound
    // to absorb real Docker/Postgres round-trip overhead without being so loose it would also
    // pass under the serialized/starved pre-fix behavior (empirically ~5.7s+ -- see class-level
    // Fact for the manual RED verification methodology).
    private static readonly TimeSpan MaxAcceptableWallClock = TimeSpan.FromSeconds(6);

    public AsyncConnectionAcquisitionIntegrationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return new[] { SupportedDatabase.PostgreSql };
    }

    /// <summary>
    /// Builds a fresh DatabaseContext pointed at the same Testcontainers PostgreSQL instance the
    /// shared fixture context uses, but with its own small reader pool -- isolated from whatever
    /// pool size the shared fixture context itself uses for other tests in this collection.
    /// Uses the real (non-redacted) connection string via IntegrationTestFixture.GetRawConnectionString
    /// -- IDatabaseContext.ConnectionString itself is deliberately password-redacted for safe
    /// logging/display and is not usable to actually connect.
    /// </summary>
    private DatabaseContext CreateSmallPoolContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = Fixture.GetRawConnectionString(SupportedDatabase.PostgreSql),
            ProviderName = "Npgsql",
            MaxConcurrentReads = PoolSize,
            EnableMetrics = true
        };

        return new DatabaseContext(config, NpgsqlFactory.Instance);
    }

    [SkippableFact]
    public async Task ConcurrentRealReads_ExceedingPoolSize_CompleteInPipelinedTime_NotSerializedTime()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async _ =>
        {
            using var context = CreateSmallPoolContext();

            var snapshot = context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
            Assert.Equal(PoolSize, snapshot.MaxSlots);

            var stopwatch = Stopwatch.StartNew();

            await Parallel.ForEachAsync(
                Enumerable.Range(0, ConcurrentOperations),
                new ParallelOptions { MaxDegreeOfParallelism = ConcurrentOperations },
                async (_, ct) =>
                {
                    await using var sc = context.CreateSqlContainer("SELECT pg_sleep(");
                    sc.Query.Append(QueryDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sc.Query.Append(")");
                    await using var reader = await sc.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, ct);
                    await reader.ReadAsync(ct);
                });

            stopwatch.Stop();

            Output.WriteLine(
                $"{ConcurrentOperations} real Postgres reads over a {PoolSize}-slot pool completed in " +
                $"{stopwatch.Elapsed.TotalSeconds:F2}s (ideal pipelined ~{ConcurrentOperations / PoolSize * QueryDelaySeconds:F1}s; " +
                $"threshold {MaxAcceptableWallClock.TotalSeconds:F1}s).");

            var finalSnapshot = context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
            Output.WriteLine(
                $"Pool telemetry: PeakInUse={finalSnapshot.PeakInUse} PeakQueued={finalSnapshot.PeakQueued} " +
                $"AvgWaitMs={finalSnapshot.AverageWaitMs:F1} TotalSlotTimeouts={finalSnapshot.TotalSlotTimeouts}");

            Assert.True(
                stopwatch.Elapsed < MaxAcceptableWallClock,
                $"Expected {ConcurrentOperations} concurrent real reads over a {PoolSize}-slot pool to complete " +
                $"in pipelined time (< {MaxAcceptableWallClock.TotalSeconds:F1}s), but took " +
                $"{stopwatch.Elapsed.TotalSeconds:F2}s. This is the exact latency-cliff shape of the sync-over-async " +
                "PoolGovernor.Acquire() defect (see DatabaseContext.ConnectionLifecycle.cs AcquireSlotAsync) -- " +
                "excess waiters blocking real CLR ThreadPool threads instead of awaiting asynchronously.");

            Assert.Equal(0, finalSnapshot.TotalSlotTimeouts);
        });
    }
}
