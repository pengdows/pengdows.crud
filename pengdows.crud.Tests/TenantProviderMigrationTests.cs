using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

// TEST-002: proves live provider migration for a single tenant through the real production
// pieces (TenantConnectionResolver, TenantContextRegistry, a shared singleton gateway) rather
// than at the unit level of any one component in isolation.
public class TenantProviderMigrationTests
{
    [Table("migrate_items")]
    private class MigrateItem
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task Tenant_ReRegisteredToDifferentProvider_SharedGatewayUsesNewDialectAfterInvalidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("provider-sqlite",
            (_, _) => new fakeDbFactory(SupportedDatabase.Sqlite));
        services.AddKeyedSingleton<DbProviderFactory>("provider-mysql",
            (_, _) => new fakeDbFactory(SupportedDatabase.MySql));
        using var provider = services.BuildServiceProvider();

        var resolver = new TenantConnectionResolver();
        resolver.Register("migrate-me", new DatabaseContextConfiguration
        {
            ProviderName = "provider-sqlite",
            ConnectionString = "Data Source=migrate;EmulatedProduct=Sqlite"
        });

        var createdEvents = new List<IDatabaseContext>();
        var removedEvents = new List<IDatabaseContext>();

        using var registry = new TenantContextRegistry(
            provider,
            resolver,
            new DefaultDatabaseContextFactory(),
            provider.GetRequiredService<ILoggerFactory>());
        registry.ContextCreated += ctx => createdEvents.Add(ctx);
        registry.ContextRemoved += ctx => removedEvents.Add(ctx);

        // Step 1: resolve through provider A and execute through a shared gateway.
        var contextA = registry.GetContext("migrate-me");
        Assert.Single(createdEvents);
        Assert.Equal(SupportedDatabase.Sqlite, contextA.Product);

        var gateway = new TableGateway<MigrateItem, int>(contextA);
        Assert.True(await gateway.CreateAsync(new MigrateItem { Name = "in-sqlite" }, contextA));

        // Step 2: re-register the SAME tenant ID for a different provider, then invalidate to
        // pick up the change (the documented rotation recipe: re-register, then Invalidate).
        resolver.Register("migrate-me", new DatabaseContextConfiguration
        {
            ProviderName = "provider-mysql",
            ConnectionString = "Data Source=migrate;EmulatedProduct=MySql"
        });

        registry.Invalidate("migrate-me");
        Assert.Single(removedEvents);
        Assert.Same(contextA, removedEvents[0]);
        Assert.True(contextA.IsDisposed);

        // Step 3: the next GetContext call must create a genuinely new context bound to the new
        // provider/dialect, not resurrect or reuse the disposed one.
        var contextB = registry.GetContext("migrate-me");
        Assert.Equal(2, createdEvents.Count);
        Assert.NotSame(contextA, contextB);
        Assert.Equal(SupportedDatabase.MySql, contextB.Product);
        Assert.False(contextB.IsDisposed);

        // Step 4: the SAME shared gateway instance — constructed back when the tenant was still
        // on provider A — must execute correctly against the new provider/dialect when routed
        // through contextB, with no reconstruction needed.
        Assert.True(await gateway.CreateAsync(new MigrateItem { Name = "in-mysql" }, contextB));
    }
}
