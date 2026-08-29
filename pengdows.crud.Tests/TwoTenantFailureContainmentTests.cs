using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

// TEST-001: proves the documented context-per-tenant isolation claim end-to-end through a real
// shared singleton gateway, not just at the raw PoolGovernor unit level. One TableGateway
// instance is constructed once (per CLAUDE.md's multi-tenancy pattern) and reused across two
// independently governed tenant contexts passed per-call.
public class TwoTenantFailureContainmentTests
{
    [Table("items")]
    private class Item
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task SaturatedWriterGovernor_OnOneTenant_DoesNotAffectAnotherTenant_OnSharedSingletonGateway()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<Item>();

        var configA = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=tenant-a;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(200)
        };
        using var tenantA = new DatabaseContext(configA, new fakeDbFactory(SupportedDatabase.Sqlite), null, typeMap);

        var configB = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=tenant-b;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(5)
        };
        using var tenantB = new DatabaseContext(configB, new fakeDbFactory(SupportedDatabase.Sqlite));

        // One shared singleton gateway, constructed against tenant A — matches the documented
        // multi-tenancy pattern where a per-call context (not the constructor context) routes
        // each individual operation.
        var gateway = new TableGateway<Item, int>(tenantA);

        // Saturate tenant A's single write slot by holding a write connection open without
        // releasing it back to the governor.
        var heldConnection = tenantA.GetConnection(ExecutionType.Write);
        try
        {
            // Tenant A must now fail fast (short PoolAcquireTimeout) rather than hang or succeed.
            await Assert.ThrowsAsync<PoolSaturatedException>(
                () => gateway.CreateAsync(new Item { Id = 1, Name = "a" }, tenantA).AsTask());

            // Tenant B, routed through the SAME gateway instance, must be completely unaffected —
            // independent governor, independent queue, independent metrics.
            var createdB = await gateway.CreateAsync(new Item { Id = 2, Name = "b" }, tenantB);
            Assert.True(createdB);

            var snapshotA = tenantA.GetPoolStatisticsSnapshot(PoolLabel.Writer);
            var snapshotB = tenantB.GetPoolStatisticsSnapshot(PoolLabel.Writer);

            // Tenant A: only the one held connection ever got a slot — the failed CreateAsync
            // attempt never acquired one, proving its own governor genuinely rejected it rather
            // than silently borrowing capacity from somewhere else.
            Assert.Equal(1, snapshotA.InUse);
            Assert.Equal(1, snapshotA.TotalAcquired);

            // Tenant B: its own governor admitted its own write, completely independent of
            // tenant A's saturation — zero timeouts recorded on tenant B's pool.
            Assert.Equal(0, snapshotB.TotalSlotTimeouts);
            Assert.True(snapshotB.TotalAcquired >= 1,
                "Tenant B's writer pool should show its own successful acquire, independent of tenant A's saturation.");
        }
        finally
        {
            tenantA.CloseAndDisposeConnection(heldConnection);
        }
    }
}
