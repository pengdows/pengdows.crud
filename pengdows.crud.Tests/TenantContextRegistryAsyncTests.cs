using System;
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

// Code-review finding: DatabaseContext.CreateAsync gives callers a genuinely non-blocking
// construction path, but TenantContextRegistry — the multi-tenancy subsystem most likely to
// construct many contexts, e.g. on first request per tenant — had no way to reach it:
// CreateDatabaseContext() always called the synchronous IDatabaseContextFactory.Create(...).
// These tests drive the new GetContextAsync API and prove it actually uses CreateAsync (not a
// fake-async wrapper around the sync path), shares one cache with the synchronous GetContext,
// and preserves the existing dedup/cap/disposal guarantees for the async path too.
public class TenantContextRegistryAsyncTests
{
    private sealed class StubResolver : ITenantConnectionResolver
    {
        private readonly IDatabaseContextConfiguration _cfg;
        public StubResolver(IDatabaseContextConfiguration cfg) => _cfg = cfg;
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant) => _cfg;
    }

    // Throws if Create() (the synchronous method) is ever invoked, proving GetContextAsync
    // routes through CreateAsync exclusively rather than falling back to a blocking call.
    private sealed class RecordingAsyncOnlyContextFactory : IDatabaseContextFactory
    {
        private int _asyncCallCount;
        public int AsyncCallCount => _asyncCallCount;

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            throw new InvalidOperationException(
                "GetContextAsync must not call the synchronous Create() method.");
        }

        public Task<IDatabaseContext> CreateAsync(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asyncCallCount);
            return Task.FromResult<IDatabaseContext>(new DatabaseContext(configuration, factory, loggerFactory));
        }
    }

    // Blocks inside CreateAsync until released, so a test can force two concurrent
    // GetContextAsync calls for the same new tenant to race the install step.
    private sealed class BlockingAsyncContextFactory : IDatabaseContextFactory
    {
        private readonly SemaphoreSlim _creationStarted;
        private readonly SemaphoreSlim _proceedWithCreation;
        private int _callCount;
        public int CallCount => _callCount;

        public BlockingAsyncContextFactory(SemaphoreSlim creationStarted, SemaphoreSlim proceedWithCreation)
        {
            _creationStarted = creationStarted;
            _proceedWithCreation = proceedWithCreation;
        }

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            throw new InvalidOperationException("Not used by this test.");
        }

        public async Task<IDatabaseContext> CreateAsync(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory, CancellationToken cancellationToken = default)
        {
            var callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber <= 2)
            {
                _creationStarted.Release();
                await _proceedWithCreation.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

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

    [Fact]
    public async Task GetContextAsync_ResolvesContext_UsingOnlyTheAsyncFactoryMethod()
    {
        var cfg = MakeConfig();
        using var provider = (ServiceProvider)BuildProvider();
        var factory = new RecordingAsyncOnlyContextFactory();
        await using var registry = new TenantContextRegistry(provider, new StubResolver(cfg), factory,
            provider.GetRequiredService<ILoggerFactory>());

        var ctx = await registry.GetContextAsync("tenant-async-1");

        Assert.Equal(1, factory.AsyncCallCount);
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        var affected = await sc.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task GetContextAsync_ForAlreadyCachedTenant_ReturnsSameInstanceAsSyncGetContext()
    {
        var cfg = MakeConfig();
        using var provider = (ServiceProvider)BuildProvider();
        var factory = new RecordingAsyncOnlyContextFactory();
        await using var registry = new TenantContextRegistry(provider, new StubResolver(cfg), factory,
            provider.GetRequiredService<ILoggerFactory>());

        var fromAsync = await registry.GetContextAsync("tenant-shared");
        var fromSync = registry.GetContext("tenant-shared");

        Assert.Same(fromAsync, fromSync);
        Assert.Equal(1, factory.AsyncCallCount);
    }

    [Fact]
    public async Task GetContextAsync_CalledConcurrentlyForSameNewTenant_ResultsInExactlyOneCachedContext()
    {
        var cfg = MakeConfig();
        using var provider = (ServiceProvider)BuildProvider();
        using var creationStarted = new SemaphoreSlim(0, 2);
        using var proceedWithCreation = new SemaphoreSlim(0, 2);
        var factory = new BlockingAsyncContextFactory(creationStarted, proceedWithCreation);
        await using var registry = new TenantContextRegistry(provider, new StubResolver(cfg), factory,
            provider.GetRequiredService<ILoggerFactory>());

        var first = registry.GetContextAsync("tenant-race");
        var second = registry.GetContextAsync("tenant-race");

        await creationStarted.WaitAsync();
        await creationStarted.WaitAsync();
        proceedWithCreation.Release(2);

        var results = await Task.WhenAll(first, second);

        Assert.Same(results[0], results[1]);
        Assert.Same(results[0], registry.GetContext("tenant-race"));
    }

    [Fact]
    public async Task GetContextAsync_WhenMaxTenantCountReached_ThrowsForNewTenant()
    {
        var cfg = MakeConfig();
        using var provider = (ServiceProvider)BuildProvider();
        var factory = new RecordingAsyncOnlyContextFactory();
        await using var registry = new TenantContextRegistry(provider, new StubResolver(cfg), factory,
            provider.GetRequiredService<ILoggerFactory>(), maxTenantCount: 1);

        await registry.GetContextAsync("tenant-cap-1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.GetContextAsync("tenant-cap-2"));
    }

    [Fact]
    public async Task GetContextAsync_AfterRegistryDisposed_ThrowsObjectDisposedException()
    {
        var cfg = MakeConfig();
        using var provider = (ServiceProvider)BuildProvider();
        var factory = new RecordingAsyncOnlyContextFactory();
        var registry = new TenantContextRegistry(provider, new StubResolver(cfg), factory,
            provider.GetRequiredService<ILoggerFactory>());
        await registry.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => registry.GetContextAsync("tenant-after-dispose"));
    }

    [Fact]
    public async Task GetContextAsync_WithNullOrWhitespaceTenant_ThrowsArgumentNullException()
    {
        var cfg = MakeConfig();
        using var provider = (ServiceProvider)BuildProvider();
        var factory = new RecordingAsyncOnlyContextFactory();
        await using var registry = new TenantContextRegistry(provider, new StubResolver(cfg), factory,
            provider.GetRequiredService<ILoggerFactory>());

        await Assert.ThrowsAsync<ArgumentNullException>(() => registry.GetContextAsync("   "));
    }
}
