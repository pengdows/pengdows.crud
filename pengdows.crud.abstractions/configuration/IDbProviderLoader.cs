#region

using Microsoft.Extensions.DependencyInjection;

#endregion

namespace pengdows.crud.configuration;

/// <summary>
/// Loads and registers database providers with the application service container.
/// </summary>
public interface IDbProviderLoader
{
    /// <summary>
    /// Adds provider-specific services to the supplied collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    void LoadAndRegisterProviders(IServiceCollection services);
}