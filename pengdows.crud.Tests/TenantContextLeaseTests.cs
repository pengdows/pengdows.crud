using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

// CORE-010: a caller holding a bare IDatabaseContext from GetContext/GetContextAsync has no
// protection against a concurrent Invalidate/InvalidateAll disposing it. These tests drive the
// additive ITenantContextRegistry.AcquireLease/AcquireLeaseAsync API, which closes that gap for
// callers who opt into it: a leased context is guaranteed not to be disposed by Invalidate until
// every outstanding lease on it has been released.
public class TenantContextLeaseTests
{
    private sealed class StubResolver : ITenantConnectionResolver
    {
        private readonly IDatabaseContextConfiguration _cfg;
        public StubResolver(IDatabaseContextConfiguration cfg) => _cfg = cfg;
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant) => _cfg;
    }

    private sealed class RealContextFactory : IDatabaseContextFactory
    {
        private int _createCount;
        public int CreateCount => Volatile.Read(ref _createCount);

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            Interlocked.Increment(ref _createCount);
            return new DatabaseContext(configuration, factory, loggerFactory);
        }
    }

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));
        return services.BuildServiceProvider();
    }

    private static DatabaseContextConfiguration MakeConfig() => new()
    {
        ProviderName = "fake-sqlite",
        ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
    };

    private static TenantContextRegistry BuildRegistry(IServiceProvider provider, IDatabaseContextConfiguration cfg,
        IDatabaseContextFactory factory)
    {
        return new TenantContextRegistry(provider, new StubResolver(cfg), factory,
            provider.GetRequiredService<ILoggerFactory>());
    }

    private static async Task<bool> IsUsableAsync(IDatabaseContext context)
    {
        try
        {
            await using var sc = context.CreateSqlContainer("SELECT 1");
            await sc.ExecuteNonQueryAsync();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    // A lease's last release/an Invalidate that finds an idle entry both trigger disposal via
    // TenantContextRegistry.ScheduleDisposeEntry, which dispatches onto the thread pool rather
    // than running inline on the releasing/invalidating caller's thread (confirmed necessary
    // empirically — see ScheduleDisposeEntry's doc comment). So disposal can lag slightly behind
    // the call that triggered it returning; poll with a generous bound instead of asserting
    // immediately.
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task AcquireLease_ProtectsContextFromInvalidateWhileHeld()
    {
        using var provider = (ServiceProvider)BuildProvider();
        var cfg = MakeConfig();
        await using var registry = BuildRegistry(provider, cfg, new RealContextFactory());

        var removedContexts = new List<IDatabaseContext>();
        registry.ContextRemoved += ctx => removedContexts.Add(ctx);

        var lease = registry.AcquireLease("tenant-a");
        var context = lease.Context;

        registry.Invalidate("tenant-a");

        // Still protected — Invalidate must defer actual disposal while the lease is held.
        Assert.True(await IsUsableAsync(context));
        Assert.Empty(removedContexts);

        await lease.DisposeAsync();

        // Now that the only lease released, deferred disposal must have fired.
        await WaitUntilAsync(() => removedContexts.Count > 0);
        Assert.Single(removedContexts);
        Assert.Same(context, removedContexts[0]);
        Assert.False(await IsUsableAsync(context));
    }

    [Fact]
    public async Task AcquireLease_MultipleLeasesOnSameTenant_DisposesOnlyAfterAllReleased()
    {
        using var provider = (ServiceProvider)BuildProvider();
        var cfg = MakeConfig();
        await using var registry = BuildRegistry(provider, cfg, new RealContextFactory());

        var removedCount = 0;
        registry.ContextRemoved += _ => Interlocked.Increment(ref removedCount);

        var lease1 = registry.AcquireLease("tenant-b");
        var lease2 = registry.AcquireLease("tenant-b");
        Assert.Same(lease1.Context, lease2.Context);

        registry.Invalidate("tenant-b");

        await lease1.DisposeAsync();
        Assert.Equal(0, removedCount);
        Assert.True(await IsUsableAsync(lease2.Context));

        await lease2.DisposeAsync();
        await WaitUntilAsync(() => Volatile.Read(ref removedCount) > 0);
        Assert.Equal(1, removedCount);
    }

    [Fact]
    public async Task AcquireLease_OnAlreadyInvalidatedIdleTenant_ConstructsFreshContext()
    {
        using var provider = (ServiceProvider)BuildProvider();
        var cfg = MakeConfig();
        await using var registry = BuildRegistry(provider, cfg, new RealContextFactory());

        using (var firstLease = registry.AcquireLease("tenant-c"))
        {
            registry.Invalidate("tenant-c"); // no other leases outstanding -> disposes immediately
        }

        await using var secondLease = registry.AcquireLease("tenant-c");

        Assert.NotNull(secondLease.Context);
        Assert.True(await IsUsableAsync(secondLease.Context));
    }

    [Fact]
    public async Task AcquireLease_ConcurrentReleaseAndInvalidateHammer_DisposesExactlyOnce()
    {
        // The exact race an earlier draft of this design got wrong: a plain Interlocked.Increment
        // lease counter can't detect "disposal already committed," letting a lease resurrect a
        // reference to an already-disposed context. Hammer AcquireLease/Dispose concurrently with
        // Invalidate and assert the context is only ever torn down exactly once, and every lease
        // that was successfully handed out was genuinely usable at the moment it was returned.
        //
        // AcquireLease is synchronous/blocking by design (see GetContext's identical contract) —
        // driving it at high fan-out here uses TaskCreationOptions.LongRunning (a dedicated thread
        // per task) rather than plain Task.Run. Task.Run schedules onto the shared thread pool,
        // and a blocking call that only one of N queued pool-thread delegates can complete first
        // (the Lazy<Task<T>> single-flight winner) while the other N-1 block waiting on it is a
        // classic self-inflicted thread-pool starvation pattern — confirmed directly: the exact
        // same scenario via plain Task.Run took ~60-90s to resolve (the pool's slow thread-
        // injection heuristic eventually breaking the cycle) while LongRunning resolves in
        // milliseconds. That starvation risk is a property of blocking-call-under-Task.Run at high
        // fan-out in general, not specific to this registry — the pre-existing synchronous
        // GetContext/Lazy<IDatabaseContext> path has the identical characteristic.
        using var provider = (ServiceProvider)BuildProvider();
        var cfg = MakeConfig();
        var factory = new RealContextFactory();
        await using var registry = BuildRegistry(provider, cfg, factory);

        var removedCount = 0;
        registry.ContextRemoved += _ => Interlocked.Increment(ref removedCount);

        const int iterations = 32;
        var tasks = new List<Task>();
        for (var i = 0; i < iterations; i++)
        {
            tasks.Add(Task.Factory.StartNew(async () =>
            {
                var lease = registry.AcquireLease("tenant-hammer");
                Assert.True(await IsUsableAsync(lease.Context));
                await Task.Yield();
                lease.Dispose();
            }, TaskCreationOptions.LongRunning).Unwrap());
        }

        tasks.Add(Task.Factory.StartNew(() => registry.Invalidate("tenant-hammer"), TaskCreationOptions.LongRunning));

        await Task.WhenAll(tasks);
        registry.Invalidate("tenant-hammer");

        // Every AcquireLease call above disposes its own lease before its task completes, so by
        // this point every context ever constructed during the hammer has had all its leases
        // released — except possibly the very last surviving (never-invalidated) entry, which
        // the trailing Invalidate accounts for. Exact-count assertion: every constructed context
        // must be disposed exactly once — no leaks (never-disposed), no double-dispose.
        await WaitUntilAsync(() => Volatile.Read(ref removedCount) >= factory.CreateCount);
        Assert.Equal(factory.CreateCount, removedCount);
    }
}
