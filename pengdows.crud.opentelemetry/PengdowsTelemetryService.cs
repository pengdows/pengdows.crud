using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using pengdows.crud.tenant;

namespace pengdows.crud.opentelemetry;

/// <summary>
/// Background service that auto-discovers IDatabaseContext instances and registers them with the observer.
/// </summary>
public sealed class PengdowsTelemetryService(
    IServiceProvider services,
    IPengdowsMetricsObserver observer) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to registry lifecycle events so tenant contexts are tracked and
        // untracked immediately — without waiting for the next poll interval.
        var registry = services.GetService<ITenantContextRegistry>();
        Action<IDatabaseContext>? onCreated = null;
        Action<IDatabaseContext>? onRemoved = null;

        if (registry != null)
        {
            onCreated = ctx => observer.Track(ctx);
            onRemoved = ctx => observer.Untrack(ctx);
            registry.ContextCreated += onCreated;
            registry.ContextRemoved += onRemoved;
        }

        try
        {
            // Initial discovery of already-registered singleton contexts.
            Discover(services);

            // Continue polling so that any DI-registered contexts added after startup
            // (non-tenant, late-bound) are still picked up.
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Discover(services);
            }
        }
        finally
        {
            // Always unsubscribe from registry events on shutdown to prevent
            // callbacks arriving after the observer may have been disposed.
            if (registry != null)
            {
                if (onCreated != null) registry.ContextCreated -= onCreated;
                if (onRemoved != null) registry.ContextRemoved -= onRemoved;
            }
        }
    }

    private void Discover(IServiceProvider sp)
    {
        foreach (var context in sp.GetServices<IDatabaseContext>())
        {
            observer.Track(context);
        }
    }
}
