#region

using Microsoft.Extensions.DependencyInjection;

#endregion

namespace pengdows.crud.configuration;

public interface IDbProviderLoader
{
    void LoadAndRegisterProviders(IServiceCollection services);
}