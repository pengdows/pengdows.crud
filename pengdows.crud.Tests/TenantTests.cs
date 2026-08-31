using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

public class TenantTests
{
    // Invalidate's disposal (via TenantContextRegistry.ScheduleDisposeEntry) is dispatched onto
    // the thread pool rather than run inline on the invalidating caller's thread, so it can lag
    // slightly behind Invalidate returning. Poll with a generous bound instead of asserting
    // immediately.
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private sealed class StubResolver : ITenantConnectionResolver
    {
        private readonly IDatabaseContextConfiguration _cfg;

        public StubResolver(IDatabaseContextConfiguration cfg)
        {
            _cfg = cfg;
        }

        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
        {
            return _cfg;
        }
    }

    private sealed class StubContextFactory : IDatabaseContextFactory
    {
        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            return new DatabaseContext(configuration, factory, loggerFactory);
        }
    }

    // Throws on the first Create call; succeeds on all subsequent calls.
    private sealed class ThrowOnFirstCallFactory : IDatabaseContextFactory
    {
        private readonly IDatabaseContextConfiguration _cfg;
        private int _callCount;

        public ThrowOnFirstCallFactory(IDatabaseContextConfiguration cfg)
        {
            _cfg = cfg;
        }

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                throw new InvalidOperationException("transient factory failure");
            }
            return new DatabaseContext(_cfg, new fakeDbFactory(SupportedDatabase.Sqlite), loggerFactory);
        }
    }

    // Always throws, simulating a permanently unavailable provider.
    private sealed class AlwaysThrowsFactory : IDatabaseContextFactory
    {
        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            throw new InvalidOperationException("factory always fails");
        }
    }

    // Deterministically pauses inside Create() so a test can inject a race (Invalidate,
    // registry Dispose) between the moment GetContext installs the Lazy and the moment its
    // factory delegate actually completes.
    private sealed class BlockingContextFactory : IDatabaseContextFactory
    {
        private readonly SemaphoreSlim _creationStarted;
        private readonly SemaphoreSlim _proceedWithCreation;
        private int _callCount;

        public BlockingContextFactory(SemaphoreSlim creationStarted, SemaphoreSlim proceedWithCreation)
        {
            _creationStarted = creationStarted;
            _proceedWithCreation = proceedWithCreation;
        }

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            // Only the first call blocks — a fixed implementation may retry and call this
            // factory again after disposing an orphan, and a naive test double that blocks on
            // every call would deadlock that (correct) retry forever on an already-consumed
            // semaphore instead of exercising it.
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                _creationStarted.Release();
                _proceedWithCreation.Wait();
            }

            return new DatabaseContext(configuration, factory, loggerFactory);
        }
    }

    // TEST-008: like BlockingContextFactory, but blocks on a caller-chosen call number (not
    // always the first) and exposes the observed call count — needed to force a genuine race
    // specifically on a RECREATION after an initial, uneventful creation, rather than on the
    // very first call ever made to the factory.
    private sealed class BlockingOnNthCallContextFactory : IDatabaseContextFactory
    {
        private readonly int _blockOnCall;
        private readonly SemaphoreSlim _creationStarted;
        private readonly SemaphoreSlim _proceedWithCreation;
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public BlockingOnNthCallContextFactory(int blockOnCall, SemaphoreSlim creationStarted,
            SemaphoreSlim proceedWithCreation)
        {
            _blockOnCall = blockOnCall;
            _creationStarted = creationStarted;
            _proceedWithCreation = proceedWithCreation;
        }

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            if (Interlocked.Increment(ref _callCount) == _blockOnCall)
            {
                _creationStarted.Release();
                _proceedWithCreation.Wait();
            }

            return new DatabaseContext(configuration, factory, loggerFactory);
        }
    }

    [Fact]
    public async Task TenantContextRegistry_ResolvesContextFromKeyedFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(provider, new StubResolver(cfg),
            new StubContextFactory(), provider.GetRequiredService<ILoggerFactory>());

        using var ctx = registry.GetContext("tenant1");
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        var affected = await sc.ExecuteNonQueryAsync();
        Assert.Equal(1, affected); // fake provider default non-query result
    }

    [Fact]
    public async Task TenantServiceCollectionExtensions_RegistersResolverAndRegistry()
    {
        var dict = new Dictionary<string, string?>
        {
            ["MultiTenant:Tenants:0:Name"] = "tenant-di-a",
            ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ProviderName"] = "fake-sqlite",
            ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ConnectionString"] =
                "Data Source=test;EmulatedProduct=Sqlite"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        services.AddMultiTenancy(configuration);
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<ITenantContextRegistry>();
        using var ctx = registry.GetContext("tenant-di-a");
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        var affected = await sc.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task GetContext_WhenFactoryThrowsOnFirstCall_SubsequentCallRetriesAndSucceeds()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new ThrowOnFirstCallFactory(cfg),
            provider.GetRequiredService<ILoggerFactory>());

        // First call: factory faults — must propagate the exception to the caller.
        Assert.Throws<InvalidOperationException>(() => registry.GetContext("tenant-retry"));

        // Second call: no Invalidate required — the faulted entry should have been removed
        // automatically, so the factory is invoked fresh and succeeds.
        using var ctx = registry.GetContext("tenant-retry");
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        var affected = await sc.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);
    }

    [Fact]
    public void GetContext_WhenFactoryAlwaysFails_InvalidateIsIdempotentAndContextRemovedNotFired()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new AlwaysThrowsFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var removedCount = 0;
        registry.ContextRemoved += _ => removedCount++;

        Assert.Throws<InvalidOperationException>(() => registry.GetContext("bad-tenant"));

        // Invalidate must not throw even though no context was ever created.
        registry.Invalidate("bad-tenant");

        Assert.Equal(0, removedCount);
    }

    // Verifies that ContextCreated fires when GetContext successfully creates a new context.
    // PengdowsTelemetryService depends on this to begin tracking the context in OTel.
    [Fact]
    public void GetContext_OnFirstSuccessfulCall_RaisesContextCreated()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new StubContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var createdContexts = new List<IDatabaseContext>();
        registry.ContextCreated += ctx => createdContexts.Add(ctx);

        using var returned = registry.GetContext("new-tenant");

        Assert.Single(createdContexts);
        Assert.Same(returned, createdContexts[0]);
    }

    // Verifies that ContextCreated fires exactly once even when the same tenant key
    // is requested multiple times — the second call returns the cached context.
    [Fact]
    public void GetContext_OnSubsequentCalls_DoesNotRaiseContextCreatedAgain()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new StubContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var fireCount = 0;
        registry.ContextCreated += _ => fireCount++;

        registry.GetContext("cached-tenant");
        registry.GetContext("cached-tenant");
        registry.GetContext("cached-tenant");

        Assert.Equal(1, fireCount);
    }

    // Verifies that ContextCreated is NOT raised when the factory faults — a context
    // that was never successfully constructed must not appear in OTel tracking.
    [Fact]
    public void GetContext_WhenFactoryFaults_DoesNotRaiseContextCreated()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new AlwaysThrowsFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var fireCount = 0;
        registry.ContextCreated += _ => fireCount++;

        Assert.Throws<InvalidOperationException>(() => registry.GetContext("bad-tenant"));

        Assert.Equal(0, fireCount);
    }

    // Proves the concurrent-remove invariant: many threads faulting on the same
    // tenant key all receive the original exception. TryRemove(KeyValuePair) in
    // GetContext guarantees idempotency — the first remover succeeds, the rest
    // are no-ops. No caller should observe NullReferenceException,
    // ObjectDisposedException, or any type other than InvalidOperationException.
    [Fact]
    public async Task GetContext_WhenManyConcurrentFaultingCallers_AllGetExceptionWithoutCorruption()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new AlwaysThrowsFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var caught = new ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    registry.GetContext("shared-tenant");
                }
                catch (Exception ex)
                {
                    caught.Add(ex);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(20, caught.Count);
        Assert.All(caught, ex => Assert.IsType<InvalidOperationException>(ex));
    }

    // CORE-009: TenantConnectionResolver resolves tenant configuration case-insensitively
    // (StringComparer.OrdinalIgnoreCase), but TenantContextRegistry's internal
    // ConcurrentDictionary<string, Lazy<IDatabaseContext>> previously used the default
    // case-sensitive comparer. "tenant-x" and "TENANT-X" both resolve to the same
    // configuration but must also share exactly one cached context — otherwise the registry
    // silently creates two independently governed contexts (and consumes two cardinality
    // slots) for what the resolver considers a single tenant.
    [Fact]
    public void GetContext_WithDifferentCasing_ReturnsSameContextAndRaisesContextCreatedOnce()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new StubContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var fireCount = 0;
        registry.ContextCreated += _ => fireCount++;

        var lower = registry.GetContext("tenant-case");
        var upper = registry.GetContext("TENANT-CASE");
        var mixed = registry.GetContext("Tenant-Case");

        Assert.Same(lower, upper);
        Assert.Same(lower, mixed);
        Assert.Equal(1, fireCount);
    }

    // Concurrent variant of the case-insensitivity invariant above: many callers racing with
    // different casings of the same tenant ID must still converge on exactly one created
    // context and exactly one ContextCreated event.
    [Fact]
    public async Task GetContext_WithMixedCaseConcurrentCallers_ReturnsOneContextAndOneCreationEvent()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new StubContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var fireCount = 0;
        registry.ContextCreated += _ => Interlocked.Increment(ref fireCount);

        var casings = new[] { "shared-Case", "SHARED-CASE", "shared-case", "Shared-Case" };
        var results = new ConcurrentBag<IDatabaseContext>();

        var tasks = Enumerable.Range(0, 40)
            .Select(i => Task.Run(() => results.Add(registry.GetContext(casings[i % casings.Length]))))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, fireCount);
        Assert.Single(results.Distinct());
    }

    // CORE-011: ContextCreated?.Invoke(context) previously threw directly out of
    // CreateDatabaseContext when a subscriber faulted. The Lazy<IDatabaseContext> wrapping it
    // gets evicted (ResolveLazy's own fault-handling), but the already-constructed `context` —
    // a real DatabaseContext potentially holding open governors/connections — was never
    // reachable by anyone and never disposed: a leak on every faulting subscriber.
    [Fact]
    public void GetContext_WhenContextCreatedSubscriberThrows_DisposesTheCreatedContext()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new StubContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        IDatabaseContext? captured = null;
        registry.ContextCreated += ctx =>
        {
            captured = ctx;
            throw new InvalidOperationException("subscriber boom");
        };

        Assert.Throws<InvalidOperationException>(() => registry.GetContext("bad-subscriber-tenant"));

        Assert.NotNull(captured);
        Assert.True(captured!.IsDisposed,
            "The context created for a faulting ContextCreated subscriber must be disposed — " +
            "nobody else can ever reach it to dispose it themselves.");
    }

    // Isolation half of CORE-011: one faulting subscriber must not prevent other, well-behaved
    // subscribers from being notified. PengdowsTelemetryService and application code may both
    // subscribe; one broken observer should not silently blind the other.
    [Fact]
    public void GetContext_WhenOneContextCreatedSubscriberThrows_StillNotifiesOtherSubscribers()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new StubContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var secondSubscriberNotified = false;
        registry.ContextCreated += _ => throw new InvalidOperationException("first subscriber boom");
        registry.ContextCreated += _ => secondSubscriberNotified = true;

        Assert.Throws<InvalidOperationException>(() => registry.GetContext("multi-subscriber-tenant"));

        Assert.True(secondSubscriberNotified,
            "A well-behaved second subscriber must still be notified even though an earlier " +
            "subscriber threw.");
    }

    // CORE-010: Invalidate(tenant) can TryRemove a Lazy<IDatabaseContext> before it is
    // IsValueCreated. Because Invalidate's dispose-if-created check is then a no-op, the
    // in-flight GetContext call that owns that Lazy finishes creating a context nobody else
    // can ever reach through the registry — an orphaned, undisposed context. This test
    // deterministically reproduces the race via BlockingContextFactory and proves the
    // registry either returns the tracked context, or disposes the orphan rather than
    // leaking it.
    [Fact]
    public async Task Invalidate_RacingWithInFlightCreate_DoesNotLeakOrphanedContext()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();

        var creationStarted = new SemaphoreSlim(0);
        var proceedWithCreation = new SemaphoreSlim(0);

        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new BlockingContextFactory(creationStarted, proceedWithCreation),
            provider.GetRequiredService<ILoggerFactory>());

        var getContextTask = Task.Run(() => registry.GetContext("race-tenant"));

        // Wait until GetContext has installed its Lazy and entered the factory.
        await creationStarted.WaitAsync();

        // Race: invalidate the tenant before its in-flight creation finishes. The Lazy is not
        // yet IsValueCreated, so Invalidate's own dispose-if-created branch does nothing.
        registry.Invalidate("race-tenant");

        // Let the blocked creation finish.
        proceedWithCreation.Release();

        var raced = await getContextTask;

        // A follow-up call must return a live, tracked context.
        var following = registry.GetContext("race-tenant");
        Assert.False(following.IsDisposed);

        if (!ReferenceEquals(raced, following))
        {
            // `raced` was the orphan produced by the evicted Lazy — it must have been disposed
            // by the registry rather than leaked as a live, untracked context.
            Assert.True(raced.IsDisposed);
        }
    }

    // Companion race: registry disposal itself (not Invalidate) wins against an in-flight
    // creation. DisposeManaged() skips any Lazy that is not yet IsValueCreated and then
    // clears the dictionary — the in-flight creation must not be allowed to hand back a live,
    // untracked context after the registry considers itself fully disposed.
    [Fact]
    public async Task Dispose_RacingWithInFlightCreate_ThrowsInsteadOfLeakingOrphanedContext()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();

        var creationStarted = new SemaphoreSlim(0);
        var proceedWithCreation = new SemaphoreSlim(0);

        var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new BlockingContextFactory(creationStarted, proceedWithCreation),
            provider.GetRequiredService<ILoggerFactory>());

        var getContextTask = Task.Run(() => registry.GetContext("dispose-race-tenant"));

        await creationStarted.WaitAsync();

        // Race: dispose the registry before the in-flight creation finishes.
        registry.Dispose();

        proceedWithCreation.Release();

        // The racing call must not return a live, untracked context past registry disposal —
        // it either throws ObjectDisposedException, or (if it does return) the context it
        // returns must already be disposed.
        try
        {
            var raced = await getContextTask;
            Assert.True(raced.IsDisposed);
        }
        catch (ObjectDisposedException)
        {
            // Acceptable: failing closed instead of leaking a live context is the safe outcome.
        }
    }

    private sealed class RecordingBlockingContextFactory : IDatabaseContextFactory
    {
        private readonly SemaphoreSlim _creationStarted;
        private readonly SemaphoreSlim _proceedWithCreation;
        private int _callCount;

        public IDatabaseContext? CreatedContext { get; private set; }

        public RecordingBlockingContextFactory(SemaphoreSlim creationStarted, SemaphoreSlim proceedWithCreation)
        {
            _creationStarted = creationStarted;
            _proceedWithCreation = proceedWithCreation;
        }

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                _creationStarted.Release();
                _proceedWithCreation.Wait();
            }

            var context = new DatabaseContext(configuration, factory, loggerFactory);
            CreatedContext = context;
            return context;
        }
    }

    // External-review finding, 2026-08-30: the sibling test above only proves the racing CALLER
    // fails closed (ObjectDisposedException) — it never inspects whether the context that was
    // actually constructed during the race gets disposed. DisposeManaged()/DisposeManagedAsync()
    // skip any entry that isn't yet IsCompletedSuccessfully and attach no continuation for it
    // (unlike Invalidate's MarkRemoved, which does) before clearing _contexts — so if construction
    // later succeeds, the resulting IDatabaseContext is unreachable through the registry AND never
    // disposed by anyone. This test captures the context a recording factory actually built and
    // proves it is eventually disposed, not merely that the caller was turned away.
    [Fact]
    public async Task Dispose_RacingWithInFlightCreate_DisposesTheOrphanedContextOnceConstructionCompletes()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();

        var creationStarted = new SemaphoreSlim(0);
        var proceedWithCreation = new SemaphoreSlim(0);
        var factory = new RecordingBlockingContextFactory(creationStarted, proceedWithCreation);

        var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            factory,
            provider.GetRequiredService<ILoggerFactory>());

        var getContextTask = Task.Run(() => registry.GetContext("dispose-race-tenant-orphan"));

        await creationStarted.WaitAsync();

        // Race: dispose the registry before the in-flight creation finishes.
        registry.Dispose();

        proceedWithCreation.Release();

        try
        {
            await getContextTask;
        }
        catch (ObjectDisposedException)
        {
            // Expected — already proven by the sibling test. This test is about what happens to
            // the context that was actually constructed, not the caller's own outcome.
        }

        var created = factory.CreatedContext;
        Assert.NotNull(created);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!created!.IsDisposed && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(created.IsDisposed,
            "A context constructed while racing registry disposal must eventually be disposed, not leaked untracked.");
    }

    // CORE-007: TenantContextRegistry's own XML remarks claimed "Contexts obtained before
    // disposal continue working normally until they are themselves disposed" — but
    // DisposeManaged/DisposeManagedAsync actually iterate and dispose every created context on
    // registry shutdown, exactly like Invalidate/InvalidateAll do for a single tenant. This test
    // pins down the REAL (and, per this fix, now correctly documented) contract: the registry
    // owns every context it creates and disposes them when the registry itself is disposed.
    [Fact]
    public void Dispose_DisposesAllContextsItCreated_RegistryOwnsCreatedContexts()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();

        var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            new StubContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());

        var context = registry.GetContext("owned-tenant");
        Assert.False(context.IsDisposed);

        registry.Dispose();

        Assert.True(context.IsDisposed,
            "The registry must dispose every context it created when the registry itself is disposed — " +
            "a context is not safe to keep using past registry disposal.");
    }

    // TEST-008: the full interleaving matrix. Existing tests already cover case-alias
    // convergence and invalidate/dispose racing an in-flight FIRST creation. The remaining gap:
    // deterministically forcing many concurrent callers to race a RECREATION after a tenant has
    // already been invalidated once, and proving EXACT counts — not just "no corruption" — for
    // construction attempts, ContextCreated/ContextRemoved events, and that no caller ever
    // observes the disposed original or an orphaned duplicate.
    [Fact]
    public async Task GetContext_ConcurrentCallersAfterInvalidation_CreateExactlyOneReplacementContext()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        using var provider = services.BuildServiceProvider();

        var creationStarted = new SemaphoreSlim(0);
        var proceedWithCreation = new SemaphoreSlim(0);

        // Block on the SECOND factory call (the recreation) — the first (the initial GetContext
        // below) must complete immediately and uneventfully so the invalidate-then-race scenario
        // starts from a clean, known state.
        var factory = new BlockingOnNthCallContextFactory(blockOnCall: 2, creationStarted, proceedWithCreation);

        using var registry = new TenantContextRegistry(
            provider,
            new StubResolver(cfg),
            factory,
            provider.GetRequiredService<ILoggerFactory>());

        var eventLock = new object();
        var createdEvents = new List<IDatabaseContext>();
        var removedEvents = new List<IDatabaseContext>();
        registry.ContextCreated += ctx =>
        {
            lock (eventLock)
            {
                createdEvents.Add(ctx);
            }
        };
        registry.ContextRemoved += ctx =>
        {
            lock (eventLock)
            {
                removedEvents.Add(ctx);
            }
        };

        var original = registry.GetContext("tenant-recreate");
        Assert.Single(createdEvents);
        Assert.Equal(1, factory.CallCount);

        registry.Invalidate("tenant-recreate");
        await WaitUntilAsync(() =>
        {
            lock (eventLock)
            {
                return removedEvents.Count > 0;
            }
        });
        Assert.Single(removedEvents);
        Assert.True(original.IsDisposed);

        const int callerCount = 8;
        var getContextTasks = Enumerable.Range(0, callerCount)
            .Select(_ => Task.Run(() => registry.GetContext("tenant-recreate")))
            .ToArray();

        // Wait until the single blocked factory call has genuinely started before releasing it,
        // proving the race is real rather than accidentally serialized.
        await creationStarted.WaitAsync();
        proceedWithCreation.Release();

        var results = await Task.WhenAll(getContextTasks);

        // Exactly one physical construction happened for the recreation (call count went from
        // 1 to 2, not to 1 + callerCount); every concurrent caller converges on that SAME new
        // instance; it is not the disposed original; and exactly one more ContextCreated event
        // fired for it — no orphaned duplicates, no phantom events.
        Assert.Equal(2, factory.CallCount);
        Assert.All(results, ctx => Assert.Same(results[0], ctx));
        Assert.NotSame(original, results[0]);
        Assert.False(results[0].IsDisposed);
        Assert.Equal(2, createdEvents.Count);
        Assert.Same(results[0], createdEvents[1]);
        Assert.Single(removedEvents);
    }
}
