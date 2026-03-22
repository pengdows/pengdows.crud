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
        // Initial discovery
        Discover(services);

        // Periodically check for new contexts (common in multi-tenant scenarios where contexts are lazy-created)
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Discover(services);
        }
    }

    private void Discover(IServiceProvider sp)
    {
        // 1. Discover singleton contexts directly from DI
        var contexts = sp.GetServices<IDatabaseContext>();
        foreach (var context in contexts)
        {
            observer.Track(context);
        }

        // 2. Discover contexts from TenantContextRegistry if available
        // Note: We can't easily 'list' tenants from the current ITenantContextRegistry interface,
        // but we can try to find the registry and see if it's an implementation we can probe,
        // or just rely on the fact that once a context is created and used, it might be in DI.
        // For now, the 30s poll covers standard singleton DI.
    }
}
