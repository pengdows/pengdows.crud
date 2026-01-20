#region

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using pengdows.crud.configuration;
using Xunit;

#endregion

namespace pengdows.crud.Tests.configuration;

public class DbProviderLoaderTests
{
    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var logger = new Mock<ILogger<DbProviderLoader>>();
        Assert.Throws<ArgumentNullException>(() => new DbProviderLoader(null!, logger.Object));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentNullException>(() => new DbProviderLoader(config, null!));
    }

    [Fact]
    public void LoadAndRegisterProviders_RegistersFactoryWithServiceCollection()
    {
        var assemblyName = typeof(PropertyFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:test:ProviderName"] = "Test.Provider",
                ["DatabaseProviders:test:FactoryType"] = typeof(PropertyFactory).FullName,
                ["DatabaseProviders:test:AssemblyName"] = assemblyName
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);
        var services = new ServiceCollection();

        loader.LoadAndRegisterProviders(services);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<DbProviderFactory>("test");

        Assert.Same(PropertyFactory.Instance, factory);
        Assert.Same(PropertyFactory.Instance, DbProviderFactories.GetFactory("Test.Provider"));
    }

    [Fact]
    public void LoadAndRegisterProviders_InvalidAssemblyPath_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:bad:ProviderName"] = "Bad",
                ["DatabaseProviders:bad:FactoryType"] = "Bad.Factory",
                ["DatabaseProviders:bad:AssemblyPath"] = "missing.dll"
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
    }

    [Fact]
    public void LoadAndRegisterProviders_InvalidFactoryType_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:sqlite:ProviderName"] = "Microsoft.Data.Sqlite",
                ["DatabaseProviders:sqlite:FactoryType"] = "Wrong.Type",
                ["DatabaseProviders:sqlite:AssemblyName"] = "Microsoft.Data.Sqlite"
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
    }

    [Fact]
    public void LoadAndRegisterProviders_RegistersUsingAssemblyPath()
    {
        var assemblyPath = Path.GetFileName(typeof(PropertyFactory).Assembly.Location);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:path:ProviderName"] = "Test.PathProvider",
                ["DatabaseProviders:path:FactoryType"] = typeof(PropertyFactory).FullName,
                ["DatabaseProviders:path:AssemblyPath"] = assemblyPath
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);
        var services = new ServiceCollection();

        loader.LoadAndRegisterProviders(services);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<DbProviderFactory>("path");

        Assert.Same(PropertyFactory.Instance, factory);
        Assert.Same(PropertyFactory.Instance, DbProviderFactories.GetFactory("Test.PathProvider"));
    }

    [Fact]
    public void LoadAndRegisterProviders_FallbackToDbProviderFactories()
    {
        var providerName = "Microsoft.Data.Sqlite.Test";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:sqlite:ProviderName"] = providerName
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);
        var services = new ServiceCollection();

        DbProviderFactories.RegisterFactory(providerName, SqliteFactory.Instance);

        loader.LoadAndRegisterProviders(services);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredKeyedService<DbProviderFactory>("sqlite");

        Assert.Same(SqliteFactory.Instance, factory);
        Assert.Same(SqliteFactory.Instance, DbProviderFactories.GetFactory(providerName));
    }

    [Fact]
    public void LoadAndRegisterProviders_InvalidProviderName_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:invalid:ProviderName"] = "Unknown.Provider"
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
    }

    [Fact]
    public void LoadAndRegisterProviders_MissingProviderName_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:missing:AssemblyName"] = "Microsoft.Data.Sqlite"
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);

        // ensure assembly load doesn't hide missing provider
        _ = SqliteFactory.Instance;

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
    }

    private class PropertyFactory : DbProviderFactory
    {
        private PropertyFactory()
        {
        }

        public static PropertyFactory Instance { get; } = new();
    }

    private class NoInstanceFactory : DbProviderFactory
    {
    }

    private class NullInstanceFactory : DbProviderFactory
    {
        public static DbProviderFactory? Instance => null;
    }

    [Fact]
    public void LoadAndRegisterProviders_MissingInstanceProperty_Throws()
    {
        var assemblyName = typeof(NoInstanceFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:noinst:ProviderName"] = "Test.NoInstance",
                ["DatabaseProviders:noinst:FactoryType"] = typeof(NoInstanceFactory).FullName,
                ["DatabaseProviders:noinst:AssemblyName"] = assemblyName
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
    }

    [Fact]
    public void LoadAndRegisterProviders_NullInstanceProperty_Throws()
    {
        var assemblyName = typeof(NullInstanceFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:nullinst:ProviderName"] = "Test.NullInstance",
                ["DatabaseProviders:nullinst:FactoryType"] = typeof(NullInstanceFactory).FullName,
                ["DatabaseProviders:nullinst:AssemblyName"] = assemblyName
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
    }

    [Fact]
    public void LoadAndRegisterProviders_BadAssemblyName_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:badname:ProviderName"] = "Bad.Name",
                ["DatabaseProviders:badname:FactoryType"] = "Bad.Factory",
                ["DatabaseProviders:badname:AssemblyName"] = "Non.Existent.Assembly"
            })
            .Build();

        var logger = new Mock<ILogger<DbProviderLoader>>();
        var loader = new DbProviderLoader(config, logger.Object);

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(new ServiceCollection()));
    }
}
