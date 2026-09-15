// =============================================================================
// FILE: TableGatewayMultiTenantDialectCacheTests.cs
// PURPOSE: Fast, fakeDb-driven proof that a single shared TableGateway<,> instance
//          generates version-correct SQL per IDatabaseContext, even when two contexts
//          are for the same product at genuinely different server versions.
//
// This is the deterministic counterpart to the real-Postgres/MySQL scenario in
// pengdows.crud.IntegrationTests/Core/MultiTenantDialectVersionTests.cs. It drives version
// detection through fakeDb's exact-command-text scalar override (SetScalarResultForCommand)
// rather than a real database round-trip, so it runs everywhere and in milliseconds.
// =============================================================================

using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayMultiTenantDialectCacheTests
{
    [Table("multi_tenant_cache_entity")]
    private sealed class CacheEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private static IDatabaseContext BuildPostgresContext(string versionString)
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<CacheEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection
        {
            ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql"
        };
        // Overrides fakeDb's hardcoded "PostgreSQL 15.0" default for "SELECT version()", by exact
        // command text rather than FIFO order - DatabaseContext's init path runs "SELECT
        // version()" more than once against the same connection (product-family detection, then
        // dialect creation/DetectDatabaseInfoAsync), and an override keyed by command text
        // answers every one of those calls identically regardless of how many round-trips the
        // current implementation happens to make.
        connection.SetScalarResultForCommand("SELECT version()", versionString);

        // DatabaseDetectionService's PostgreSQL-family flavor probes run after "SELECT version()"
        // and treat any non-empty string result as a positive match (YugabyteDB/Aurora); without
        // these, the queued Postgres version string above would itself satisfy `is string
        // { Length: > 0 }` on both probes and misidentify this as YugabyteDB.
        connection.SetScalarResultForCommand(
            "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1", DBNull.Value);
        connection.SetScalarResultForCommand("SELECT aurora_version()", DBNull.Value);

        factory.Connections.Add(connection);

        return new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=PostgreSql",
                DbMode = DbMode.SingleConnection
            },
            factory, NullLoggerFactory.Instance, typeMap);
    }

    [Fact]
    public async System.Threading.Tasks.Task SharedGateway_TwoPostgreSqlVersions_GeneratesCorrectSqlPerContext()
    {
        // PostgreSQL 15+ supports MERGE in addition to ON CONFLICT; BuildUpsert prefers MERGE
        // when the dialect supports it (checked before SupportsInsertOnConflict), so these two
        // contexts must produce genuinely different SQL statement shapes from the SAME
        // TableGateway<,> instance - exactly the multitenancy pattern in this repo's CLAUDE.md
        // (gateway.Method(entity, tenantCtx)). If the per-dialect template cache were keyed by
        // the SupportedDatabase enum (or otherwise shared across instances) instead of the
        // dialect instance, the second call below would incorrectly reuse the first context's
        // cached UpsertUpdateFragment.
        await using var oldContext = BuildPostgresContext("PostgreSQL 12.4 on x86_64-pc-linux-gnu");
        await using var newContext = BuildPostgresContext("PostgreSQL 16.1 on x86_64-pc-linux-gnu");

        Assert.False(oldContext.GetDialect().SupportsMerge, "Precondition: PostgreSQL 12.4 must not support MERGE.");
        Assert.True(newContext.GetDialect().SupportsMerge, "Precondition: PostgreSQL 16.1 must support MERGE.");

        var gateway = new TableGateway<CacheEntity, int>(oldContext);

        var oldSql = gateway.BuildUpsert(new CacheEntity { Id = 1, Name = "old-tenant" }, oldContext).Query.ToString();
        var newSql = gateway.BuildUpsert(new CacheEntity { Id = 1, Name = "new-tenant" }, newContext).Query.ToString();

        Assert.Contains("ON CONFLICT", oldSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXCLUDED", oldSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", oldSql, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("MERGE", newSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" = s.", newSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXCLUDED", newSql, StringComparison.OrdinalIgnoreCase);
    }
}
