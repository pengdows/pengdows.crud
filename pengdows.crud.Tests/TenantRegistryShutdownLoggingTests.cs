using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using pengdows.crud.tenant;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29 (Schneier: fail loudly). When the registry shuts down while a tenant context is
/// still to be constructed, it evaluates the construction itself so the context can be disposed. If
/// that construction failed, the failure was swallowed with an empty catch on both the async and the
/// sync (background work item) paths; nobody else ever observes it, so it must be logged.
/// </summary>
public sealed class TenantRegistryShutdownLoggingTests
{
    private static (TenantContextRegistry Registry, ListLoggerProvider Logs, LoggerFactory LoggerFactory) CreateWithFailingEntry()
    {
        var logs = new ListLoggerProvider();
        var loggerFactory = new LoggerFactory(new[] { logs });
        var registry = new TenantContextRegistry(
            Mock.Of<IServiceProvider>(),
            Mock.Of<ITenantConnectionResolver>(),
            Mock.Of<IDatabaseContextFactory>(),
            loggerFactory);

        var contexts = (ConcurrentDictionary<string, TenantContextRegistry.TenantContextEntry>)typeof(TenantContextRegistry)
            .GetField("_contexts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(registry)!;
        contexts["tenant-a"] = new TenantContextRegistry.TenantContextEntry(
            () => throw new InvalidOperationException("tenant construction failed"));
        return (registry, logs, loggerFactory);
    }

    private static bool Logged(ListLoggerProvider logs) =>
        logs.Entries.Any(e => e.Level >= LogLevel.Warning && e.Exception?.Message == "tenant construction failed");

    [Fact]
    public async Task DisposeAsync_TenantConstructionFailure_IsLogged()
    {
        var (registry, logs, loggerFactory) = CreateWithFailingEntry();
        using var _ = loggerFactory;

        await registry.DisposeAsync();

        Assert.True(Logged(logs));
    }

    [Fact]
    public async Task Dispose_TenantConstructionFailure_IsLoggedByTheBackgroundDisposal()
    {
        var (registry, logs, loggerFactory) = CreateWithFailingEntry();
        using var _ = loggerFactory;

        registry.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!Logged(logs) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(Logged(logs));
    }
}
