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
}
