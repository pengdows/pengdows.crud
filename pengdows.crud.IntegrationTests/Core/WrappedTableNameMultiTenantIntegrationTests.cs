using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Proves <c>BaseTableGateway</c>'s per-dialect wrapped-table-name cache
/// (<c>_wrappedTableNameCache</c>, keyed by <c>ISqlDialect.GetCacheFingerprint()</c>) generates
/// the correct SQL for a SINGLE shared <c>TableGateway&lt;TEntity,TRowID&gt;</c> instance used
/// across two tenants whose dialects disagree on whether the entity's declared <c>[Table]</c>
/// schema even applies: a real PostgreSQL tenant (<c>SupportsNamespaces = true</c>, a genuine
/// non-default schema) and a real SQLite tenant (<c>SupportsNamespaces = false</c> — the schema
/// is silently dropped, per <c>BaseTableGateway.Core.cs</c>'s <c>BuildWrappedTableName</c>).
/// <para>
/// This is the hardest realistic condition for that cache: one shared gateway, both tenants hit
/// concurrently and interleaved from many threads at once, so a mis-keyed or stale cache entry
/// would leak one tenant's schema-qualified (or bare) table name into the other tenant's
/// generated SQL — exactly the kind of cross-tenant contamination a multi-tenant application
/// mixing a namespace-having database with a namespace-less one must never see.
/// </para>
/// </summary>
[Collection("IntegrationTests")]
public class WrappedTableNameMultiTenantIntegrationTests : IAsyncLifetime
{
    private const string SchemaName = "wrap_ns";
    private const string TableName = "wrap_probe";

    private readonly ITestOutputHelper _output;
    private readonly IntegrationTestFixture _fixture;
    private IDatabaseContext? _postgres;
    private IDatabaseContext? _sqlite;

    public WrappedTableNameMultiTenantIntegrationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
    {
        _output = output;
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        if (!IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.PostgreSql) ||
            !IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.Sqlite))
        {
            // Guarded again via Skip.If in the fact body — nothing usable to set up here.
            return;
        }

        _postgres = await _fixture.CreateAdditionalContextAsync(SupportedDatabase.PostgreSql);
        _sqlite = await _fixture.CreateAdditionalContextAsync(SupportedDatabase.Sqlite);

        await using (var createSchema =
                     _postgres.CreateSqlContainer($"CREATE SCHEMA IF NOT EXISTS {_postgres.WrapObjectName(SchemaName)}"))
        {
            await createSchema.ExecuteNonQueryAsync();
        }

        await using (var createPgTable = _postgres.CreateSqlContainer($@"
CREATE TABLE IF NOT EXISTS {_postgres.WrapObjectName(SchemaName)}.{_postgres.WrapObjectName(TableName)} (
    id BIGINT PRIMARY KEY,
    tenant_tag VARCHAR(64) NOT NULL
)"))
        {
            await createPgTable.ExecuteNonQueryAsync();
        }

        await using (var createSqliteTable = _sqlite.CreateSqlContainer($@"
CREATE TABLE IF NOT EXISTS {TableName} (
    id INTEGER PRIMARY KEY,
    tenant_tag TEXT NOT NULL
)"))
        {
            await createSqliteTable.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null)
        {
            await using (var drop = _postgres.CreateSqlContainer(
                             $"DROP TABLE IF EXISTS {_postgres.WrapObjectName(SchemaName)}.{_postgres.WrapObjectName(TableName)}"))
            {
                await drop.ExecuteNonQueryAsync();
            }

            _postgres.Dispose();
        }

        if (_sqlite != null)
        {
            await using (var drop = _sqlite.CreateSqlContainer($"DROP TABLE IF EXISTS {TableName}"))
            {
                await drop.ExecuteNonQueryAsync();
            }

            _sqlite.Dispose();
        }
    }

    [SkippableFact]
    public async Task SharedGateway_MixedNamespaceSupport_GeneratesCorrectSqlPerTenant_UnderConcurrentInterleaving()
    {
        Skip.IfNot(IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.PostgreSql),
            "PostgreSQL is not enabled for this test run.");
        Skip.IfNot(IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.Sqlite),
            "SQLite is not enabled for this test run.");

        Assert.True(_postgres!.Dialect.SupportsNamespaces,
            "This test requires a namespace-capable tenant to be meaningful.");
        Assert.False(_sqlite!.Dialect.SupportsNamespaces,
            "This test requires a namespace-LESS tenant to be meaningful.");

        // ONE shared gateway instance for BOTH tenants — the documented multitenancy pattern
        // (gateway.Method(entity, tenantCtx)), and the exact scenario the wrapped-table-name
        // cache must get right per-dialect rather than per-gateway-construction-time-dialect.
        var gateway = new TableGateway<NamespaceProbeEntity, long>(_postgres);

        const int iterationsPerTenant = 25;
        var tasks = new List<Task>();

        for (var i = 0; i < iterationsPerTenant; i++)
        {
            var pgId = i * 2 + 1;
            var sqliteId = i * 2 + 2;

            // Deliberately interleaved, not sequential: both tenants' round trips are launched
            // into the thread pool together so the shared cache is genuinely raced, not just
            // reused across two well-separated phases.
            tasks.Add(RoundTripAsync(gateway, _postgres, pgId, "pg", expectSchemaQualified: true));
            tasks.Add(RoundTripAsync(gateway, _sqlite, sqliteId, "sqlite", expectSchemaQualified: false));
        }

        await Task.WhenAll(tasks);

        _output.WriteLine(
            $"Completed {iterationsPerTenant} interleaved round trips per tenant with zero cross-tenant SQL contamination.");
    }

    private static async Task RoundTripAsync(
        TableGateway<NamespaceProbeEntity, long> gateway,
        IDatabaseContext context,
        long id,
        string tenantTag,
        bool expectSchemaQualified)
    {
        var entity = new NamespaceProbeEntity { Id = id, TenantTag = tenantTag };

        await using var createContainer = gateway.BuildCreate(entity, context);
        var createSql = createContainer.Query.ToString();

        if (expectSchemaQualified)
        {
            Assert.Contains($"\"{SchemaName}\".\"{TableName}\"", createSql, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(SchemaName, createSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"{TableName}\"", createSql, StringComparison.Ordinal);
        }

        await createContainer.ExecuteNonQueryAsync();

        var retrieved = await gateway.RetrieveOneAsync(id, context);
        Assert.NotNull(retrieved);
        Assert.Equal(tenantTag, retrieved!.TenantTag);
    }

    [Table(TableName, SchemaName)]
    private class NamespaceProbeEntity
    {
        [Id]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("tenant_tag", DbType.String)]
        public string TenantTag { get; set; } = string.Empty;
    }
}
