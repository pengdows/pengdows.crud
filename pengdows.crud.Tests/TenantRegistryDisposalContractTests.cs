using System;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

// Disposing the registry disposes every context it constructed, including contexts a caller still
// holds (DisposeManaged is unconditional by design). The documentation must say so; a caller that
// needs a context to outlive the registry must not rely on it doing so.
public class TenantRegistryDisposalContractTests
{
    private sealed class FixedResolver : ITenantConnectionResolver
    {
        private readonly IDatabaseContextConfiguration _cfg;
        public FixedResolver(IDatabaseContextConfiguration cfg) => _cfg = cfg;
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant) => _cfg;
    }

    [Fact]
    public void Dispose_DisposesContextsThatCallersStillHold()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));
        using var provider = services.BuildServiceProvider();

        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };
        var registry = new TenantContextRegistry(provider, new FixedResolver(cfg),
            new pengdows.crud.DefaultDatabaseContextFactory(), provider.GetRequiredService<ILoggerFactory>());

        var held = registry.GetContext("tenant-a");
        Assert.False(held.IsDisposed);

        registry.Dispose();

        Assert.True(held.IsDisposed);
    }
}
