using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-030..032: ContextCreated/ContextRemoved handlers are application code and can throw.
/// A throwing ContextCreated handler leaked the new context; a throwing ContextRemoved handler ran
/// unprotected on a thread-pool work item (a process crash); and at shutdown one try covered both
/// the dispose and the event, so a failed dispose skipped ContextRemoved.
/// </summary>
public sealed class TenantContextEventFailureTests
{
    private sealed class StubResolver : ITenantConnectionResolver
    {
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant) => new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };
    }

    private sealed class RecordingFactory : IDatabaseContextFactory
    {
        public List<DatabaseContext> Created { get; } = new();

        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            var context = new DatabaseContext(configuration, factory, loggerFactory);
            Created.Add(context);
            return context;
        }
    }

    private static (TenantContextRegistry Registry, RecordingFactory Factory) BuildRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));
        var provider = services.BuildServiceProvider();
        var factory = new RecordingFactory();
        return (new TenantContextRegistry(provider, new StubResolver(), factory,
            provider.GetRequiredService<ILoggerFactory>()), factory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextCreatedHandlerThrows_TheNewContextIsDisposed(bool viaAsync)
    {
        var (registry, factory) = BuildRegistry();
        using var _ = registry;
        registry.ContextCreated += _ => throw new InvalidOperationException("created handler");

        var ex = await Record.ExceptionAsync(async () =>
        {
            if (viaAsync)
            {
                await registry.GetContextAsync("tenant-a");
                return;
            }

            registry.GetContext("tenant-a");
        });

        Assert.Equal("created handler", ex?.Message);
        var created = Assert.Single(factory.Created);
        Assert.True(created.IsDisposed, "The context whose ContextCreated handler threw was leaked.");
    }

    private static (TenantContextRegistry Registry, ListLoggerProvider Logs, LoggerFactory LoggerFactory)
        RegistryWithEntry(IDatabaseContext context)
    {
        var logs = new ListLoggerProvider();
        var loggerFactory = new LoggerFactory(new[] { logs });
        var registry = new TenantContextRegistry(Mock.Of<IServiceProvider>(), Mock.Of<ITenantConnectionResolver>(),
            Mock.Of<IDatabaseContextFactory>(), loggerFactory);
        var contexts = (ConcurrentDictionary<string, TenantContextRegistry.TenantContextEntry>)typeof(TenantContextRegistry)
            .GetField("_contexts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(registry)!;
        contexts["tenant-a"] = new TenantContextRegistry.TenantContextEntry(() => Task.FromResult(context));
        Assert.Same(context, registry.GetContext("tenant-a"));
        return (registry, logs, loggerFactory);
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    // Red for REV-031 aborts the test host (an unhandled exception on a thread-pool thread).
    [Fact]
    public async Task ContextRemovedHandlerThrows_OnInvalidate_IsLoggedNotUnhandled()
    {
        var (registry, logs, loggerFactory) = RegistryWithEntry(Mock.Of<IDatabaseContext>());
        using var _ = loggerFactory;
        registry.ContextRemoved += _ => throw new InvalidOperationException("removed handler");

        registry.Invalidate("tenant-a");

        Assert.True(await EventuallyAsync(() =>
            logs.Entries.Any(e => e.Level >= LogLevel.Warning && e.Exception?.Message == "removed handler")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_ContextDisposeThrows_ContextRemovedStillRaised(bool viaAsync)
    {
        var context = new Mock<IDatabaseContext>();
        context.Setup(c => c.Dispose()).Throws(new InvalidOperationException("dispose failed"));
        context.Setup(c => c.DisposeAsync()).Throws(new InvalidOperationException("dispose failed"));
        var (registry, logs, loggerFactory) = RegistryWithEntry(context.Object);
        using var _ = loggerFactory;
        var removed = new ConcurrentBag<IDatabaseContext>();
        registry.ContextRemoved += c => removed.Add(c);

        if (viaAsync)
        {
            await registry.DisposeAsync();
        }
        else
        {
            registry.Dispose();
        }

        Assert.True(await EventuallyAsync(() => removed.Contains(context.Object)),
            "ContextRemoved was skipped because the context's dispose threw.");
        Assert.Contains(logs.Entries, e => e.Exception?.Message == "dispose failed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_ContextRemovedHandlerThrows_IsLoggedAsAHandlerFailure(bool viaAsync)
    {
        var (registry, logs, loggerFactory) = RegistryWithEntry(Mock.Of<IDatabaseContext>());
        using var _ = loggerFactory;
        registry.ContextRemoved += _ => throw new InvalidOperationException("removed handler");

        if (viaAsync)
        {
            await registry.DisposeAsync();
        }
        else
        {
            registry.Dispose();
        }

        Assert.True(await EventuallyAsync(() => logs.Entries.Any(e =>
            e.Exception?.Message == "removed handler" && e.Message.Contains("ContextRemoved"))));
    }
}
