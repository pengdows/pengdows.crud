#region

using Microsoft.Extensions.DependencyInjection;

#endregion

namespace pengdow.crud.configuration;

public interface IDbProviderLoader
{
    void LoadAndRegisterProviders(IServiceCollection services);
}