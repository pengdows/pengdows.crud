using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// docs/planning/retry-context-design.md, shortcoming #9: "there is no way to obtain a RetryContext
// without already holding a tenant-scoped IDatabaseContext ... RetryContext itself would have zero
// internal tenant-awareness — it just holds whatever IDatabaseContext it was handed and never
// touches ITenantContextRegistry/ITenantContextLease at all." That analysis was "proposed, not yet
// confirmed" — this test confirms it directly, the same way TwoTenantFailureContainmentTests
// confirms context-per-tenant isolation for PoolGovernor: two independently governed contexts
// stand in for two tenants, and a RetryContext built against one must never reach the other.
public class RetryContextTenantScopingTests
{
    private static readonly RetryContextOptions FastOptions = new()
    {
        MaxAttempts = 1,
        BaseDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero
    };

    [Fact]
    public void WrappedContext_IsExactlyTheProvidedTenantContext_NotSomeOtherTenant()
    {
        var factoryA = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var tenantA = new DatabaseContext("Data Source=tenant-a;EmulatedProduct=Sqlite", factoryA);
        var factoryB = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var tenantB = new DatabaseContext("Data Source=tenant-b;EmulatedProduct=Sqlite", factoryB);

        var rc = new RetryContext(tenantA, RetryContextType.Sequential, FastOptions);

        Assert.Same(tenantA, rc.WrappedContext);
        Assert.NotSame(tenantB, rc.WrappedContext);
    }

    [Fact]
    public async Task StartAsync_AgainstOneTenant_NeverTouchesAnotherTenantsConnectionFactory()
    {
        var factoryA = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var tenantA = new DatabaseContext("Data Source=tenant-a;EmulatedProduct=Sqlite", factoryA);
        var factoryB = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var tenantB = new DatabaseContext("Data Source=tenant-b;EmulatedProduct=Sqlite", factoryB);

        var rc = new RetryContext(tenantA, RetryContextType.Sequential, FastOptions);
        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var connA = new fakeDbConnection();
        factoryA.Connections.Add(connA);
        var connB = new fakeDbConnection();
        factoryB.Connections.Add(connB);

        await rc.StartAsync();

        Assert.Contains("UPDATE \"t1\" SET \"x\" = 1", connA.ExecutedNonQueryTexts);
        // tenantB's own construction opens an initialization/detection connection independent of
        // RetryContext (unrelated to tenant scoping) — connB itself, seeded after that happened,
        // is what proves RetryContext never reached tenant B: it was never handed out or used.
        Assert.Empty(connB.ExecutedNonQueryTexts);
        Assert.DoesNotContain(connB, factoryB.CreatedConnections);
    }
}
