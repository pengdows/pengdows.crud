#region

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace pengdows.crud.configuration;

/// <summary>
/// DI extension methods for registering dynamically-configured database providers.
/// </summary>
public static class DbProviderLoaderServiceCollectionExtensions
{
    /// <summary>
    /// Loads and registers <see cref="System.Data.Common.DbProviderFactory"/> instances
    /// described by the <c>DatabaseProviders</c> section of <paramref name="configuration"/>
    /// (by assembly path, assembly name, or the legacy <c>DbProviderFactories</c> registry) as
    /// keyed singletons on <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">
    /// Configuration containing a <c>DatabaseProviders</c> section (see
    /// <see cref="DatabaseProviderConfig"/> for the shape of each entry).
    /// </param>
    /// <param name="logger">
    /// Optional logger for provider-loading diagnostics. Runs at DI composition time — before
    /// the service provider is built — so a resolved <c>ILogger&lt;DbProviderLoader&gt;</c> is
    /// never available here; defaults to <see cref="NullLogger{T}.Instance"/> when omitted.
    /// </param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddDbProviderLoading(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger<DbProviderLoader>? logger = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var loader = new DbProviderLoader(configuration, logger ?? NullLogger<DbProviderLoader>.Instance);
        loader.LoadAndRegisterProviders(services);

        return services;
    }
}
