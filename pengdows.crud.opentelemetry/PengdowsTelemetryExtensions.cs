using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace pengdows.crud.opentelemetry;

public static class PengdowsTelemetryExtensions
{
    /// <summary>
    /// Adds pengdows.crud OpenTelemetry metrics instrumentation to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddPengdowsTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<IPengdowsMetricsObserver, PengdowsMetricsObserver>();
        services.AddHostedService<PengdowsTelemetryService>();
        return services;
    }

    /// <summary>
    /// Manually tracks an <see cref="IDatabaseContext"/> instance for OpenTelemetry metrics.
    /// Useful for contexts created outside of DI or late-bound tenant contexts.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="sp">The service provider containing the <see cref="IPengdowsMetricsObserver"/>.</param>
    public static IDatabaseContext TrackPengdowsMetrics(this IDatabaseContext context, IServiceProvider sp)
    {
        var observer = sp.GetService<IPengdowsMetricsObserver>();
        observer?.Track(context);
        return context;
    }
}
