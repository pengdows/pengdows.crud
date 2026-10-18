using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// G-08 against a real database: one pool slot is held by an asynchronous caller whose query ends through real I/O
/// (<c>pg_sleep</c>), while hundreds of synchronous callers park in the blocking <c>BeginTransaction</c> behind it. The slot
/// is released by a continuation that needs a thread-pool thread; if the parked callers starved the pool, the holder would
/// finish far later than its own sleep. The measured delays are written to the test output and asserted against a generous
/// bound, so a regression into real starvation fails.
/// </summary>
[Collection("IntegrationTests")]
public class PostgreSqlSyncWaiterStarvationTests : DatabaseTestBase
{
    private const double SleepSeconds = 1.0;

    public PostgreSqlSyncWaiterStarvationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.PostgreSql);

    private static DatabaseContext OneSlotContext(IDatabaseContext admin) =>
        new(new DatabaseContextConfiguration
        {
            ConnectionString = ((DatabaseContext)admin).RawConnectionString,
            DbMode = DbMode.Standard,
            MaxConcurrentWrites = 1,
            MaxQueuedWrites = 5000,
            PoolAcquireTimeout = TimeSpan.FromSeconds(120)
        }, Npgsql.NpgsqlFactory.Instance);

    private async Task<double> HolderMilliseconds(DatabaseContext ctx, int parkedSyncCallers)
    {
        var holder = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            using var txn = await ctx.BeginTransactionAsync();
            await using (var sc = txn.CreateSqlContainer($"SELECT pg_sleep({SleepSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)})"))
            {
                await sc.ExecuteScalarOrNullAsync<object>();
            }

            txn.Commit();
            return sw.Elapsed.TotalMilliseconds;
        });

        await Task.Delay(300);
        var parked = Enumerable.Range(0, parkedSyncCallers).Select(_ => Task.Run(() =>
        {
            using var txn = ctx.BeginTransaction();
            txn.Commit();
        })).ToList();

        var elapsed = await holder;
        await Task.WhenAll(parked);
        return elapsed;
    }

    [SkippableFact]
    public async Task ParkedSynchronousCallers_DoNotDelayTheHoldersRelease()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async admin =>
        {
            await using var ctx = OneSlotContext(admin);
            await HolderMilliseconds(ctx, 0); // warm the pool and the plan caches

            var baseline = await HolderMilliseconds(ctx, 0);
            var results = new List<(int Callers, double Ms)>();
            foreach (var callers in new[] { 64, 256, 1000 })
            {
                var ms = await HolderMilliseconds(ctx, callers);
                results.Add((callers, ms));
                Output.WriteLine($"G08 parked sync callers={callers} holder={ms:F0} ms (baseline {baseline:F0} ms, minWorkers={ThreadPool.ThreadCount})");
            }

            foreach (var (callers, ms) in results)
            {
                Assert.True(ms < baseline + 5000,
                    $"{callers} parked synchronous callers delayed the holder to {ms:F0} ms (baseline {baseline:F0} ms)");
            }
        });
    }
}
