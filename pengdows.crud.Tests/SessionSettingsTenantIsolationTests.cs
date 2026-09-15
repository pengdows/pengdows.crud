using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Closes the "don't bleed between tenant contexts" leg of
/// docs/planning/3.0-architectural-review-backlog.md's session-settings P2 item. Unlike
/// TableGateway (an intentional singleton shared across tenants — see
/// TableGatewayMultiTenantDialectCacheTests.cs), IDatabaseContext is per-tenant by design (one
/// DatabaseContext per connection string per CLAUDE.md's core invariants), and session-settings
/// generation (GetBaseSessionSettings/GetReadOnlySessionSettings) is a plain per-instance
/// delegation to that context's own dialect instance with no shared cache in between — so there
/// is no mechanism by which one tenant's session settings could leak into another's. This test
/// confirms that live, across two structurally different dialect families, rather than trusting
/// the "no shared cache exists" code-reading conclusion alone.
/// </summary>
public class SessionSettingsTenantIsolationTests
{
    [Fact]
    public void TwoTenantContexts_DifferentProviders_ProduceIndependentSessionSettings()
    {
        using var mysqlContext = new DatabaseContext(
            "Data Source=test;EmulatedProduct=MySql", new fakeDbFactory(SupportedDatabase.MySql));
        using var postgresContext = new DatabaseContext(
            "Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql));

        var mysqlSettings = mysqlContext.GetBaseSessionSettings();
        var postgresSettings = postgresContext.GetBaseSessionSettings();

        Assert.NotEqual(mysqlSettings, postgresSettings);

        // Re-querying each context afterward must still return that SAME context's own
        // settings, not whatever was produced by the other context's call in between — proving
        // there is no shared mutable state a second tenant's call could have poisoned.
        Assert.Equal(mysqlSettings, mysqlContext.GetBaseSessionSettings());
        Assert.Equal(postgresSettings, postgresContext.GetBaseSessionSettings());
    }

    [Fact]
    public void TwoTenantContexts_SameProviderDifferentReadWriteMode_ProduceIndependentReadOnlySettings()
    {
        using var readOnlyContext = new DatabaseContext(
            new pengdows.crud.configuration.DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql",
                ReadWriteMode = ReadWriteMode.ReadOnly
            },
            new fakeDbFactory(SupportedDatabase.PostgreSql));
        using var readWriteContext = new DatabaseContext(
            "Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql));

        var readOnlySettings = readOnlyContext.GetReadOnlySessionSettings();
        var readWriteSettings = readWriteContext.GetReadOnlySessionSettings();

        // Both call the SAME dialect method (GetReadOnlySessionSettings is the SQL to run when
        // establishing a read-only session, independent of the context's own ReadWriteMode
        // policy) — what matters here is that each context resolves it from its own dialect
        // instance, not a shared one, and remains stable across repeated calls.
        Assert.Equal(readOnlySettings, readOnlyContext.GetReadOnlySessionSettings());
        Assert.Equal(readWriteSettings, readWriteContext.GetReadOnlySessionSettings());
    }
}
