#region

using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.configuration;
using Xunit;

#endregion

namespace pengdows.crud.Tests.configuration;

/// <summary>
/// Proves <see cref="DbProviderLoaderServiceCollectionExtensions.AddDbProviderLoading"/> is a
/// real, external-reachable entry point: a consumer with no access to <see cref="DbProviderLoader"/>'s
/// internal constructor can still register configured providers with nothing but the public
/// <see cref="IServiceCollection"/>/<see cref="IConfiguration"/> surface.
/// </summary>
public class DbProviderLoaderServiceCollectionExtensionsTests
{
    private sealed class PropertyFactory : DbProviderFactory
    {
        private PropertyFactory()
        {
        }

        public static PropertyFactory Instance { get; } = new();
    }

    [Fact]
    public void AddDbProviderLoading_RegistersConfiguredProviderWithServiceCollection()
    {
        var assemblyName = typeof(PropertyFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:test:ProviderName"] = "Test.Provider.PublicEntry",
                ["DatabaseProviders:test:FactoryType"] = typeof(PropertyFactory).FullName,
                ["DatabaseProviders:test:AssemblyName"] = assemblyName
            })
            .Build();

        var services = new ServiceCollection();

        // No internal access, no logger supplied — matches what an external consumer's
        // Program.cs/Startup.cs composition root can actually call.
        services.AddDbProviderLoading(config);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<DbProviderFactory>("test");

        Assert.Same(PropertyFactory.Instance, factory);
        Assert.Same(PropertyFactory.Instance, DbProviderFactories.GetFactory("Test.Provider.PublicEntry"));
    }

    [Fact]
    public void AddDbProviderLoading_ReturnsSameServiceCollection_ForChaining()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var result = services.AddDbProviderLoading(config);

        Assert.Same(services, result);
    }

    [Fact]
    public void AddDbProviderLoading_NullServices_Throws()
    {
        var config = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            DbProviderLoaderServiceCollectionExtensions.AddDbProviderLoading(null!, config));
    }

    [Fact]
    public void AddDbProviderLoading_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddDbProviderLoading(null!));
    }
}
