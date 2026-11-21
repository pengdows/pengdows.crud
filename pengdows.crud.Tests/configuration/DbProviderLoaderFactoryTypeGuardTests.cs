using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using Xunit;

namespace pengdows.crud.Tests.configuration;

/// <summary>
/// The loader used to invoke the static <c>Instance</c> member of whatever type the configuration named, and only
/// then check that the result was a <see cref="DbProviderFactory"/>. Static code in that member therefore ran for a
/// type that was never a factory. The type must be checked first.
/// </summary>
public class DbProviderLoaderFactoryTypeGuardTests
{
    // Not a DbProviderFactory. Its static Instance getter records that it ran.
    public sealed class NotAFactory
    {
        public static int InstanceGetterRuns;

        public static NotAFactory Instance
        {
            get
            {
                InstanceGetterRuns++;
                return new NotAFactory();
            }
        }
    }

    [Fact]
    public void FactoryTypeThatIsNotADbProviderFactory_IsRefused_WithoutRunningItsInstanceGetter()
    {
        NotAFactory.InstanceGetterRuns = 0;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:notafactory:ProviderName"] = "Not.A.Factory",
                ["DatabaseProviders:notafactory:AssemblyName"] = typeof(NotAFactory).Assembly.FullName,
                ["DatabaseProviders:notafactory:FactoryType"] = typeof(NotAFactory).FullName
            })
            .Build();
        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            loader.LoadAndRegisterProviders(new ServiceCollection()));

        Assert.Contains("not a DbProviderFactory", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, NotAFactory.InstanceGetterRuns);
    }
}
