using System.Data;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySql.Data.MySqlClient;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Feasibility probe (Priority 3 of the dialect-capability-refactor validation task, see
/// docs/FUTURE_WORK.md): proves, against TWO REAL MySQL servers at genuinely different patch
/// versions straddling the 8.0.20 <c>UpsertIncomingAlias</c> threshold
/// (<see cref="pengdows.crud.dialects.ISqlDialect"/> — MySQL 8.0.20 deprecated the legacy
/// <c>VALUES(col)</c> upsert-source syntax in favor of an aliased-row <c>incoming.col</c> form),
/// that a SINGLE shared <c>TableGateway&lt;TEntity,TRowID&gt;</c> instance generates the CORRECT,
/// version-appropriate SQL for each tenant's real server.
/// <para>
/// This is the scenario <c>TableGatewayMultiTenantDialectCacheTests.cs</c> already covers with
/// fakeDb-simulated version strings. That coverage is fast and deterministic but cannot prove two
/// things fakeDb structurally cannot: (a) that live version DETECTION (the real
/// <c>SELECT VERSION()</c> round-trip + <c>ParseVersion</c>) correctly identifies each real
/// server's version, and (b) that the per-tenant dialect instances this produces route through
/// the cache correctly under real, independently-initialized <c>IDatabaseContext</c>s rather than
/// a single fakeDb-driven context. This test proves (a): each tenant gets correct SQL for its own
/// real server version from ONE shared gateway instance.
/// </para>
/// <para>
/// <b>Scope note:</b> proving the SECOND half of the original ask — that two tenants on the
/// IDENTICAL real server version collapse to exactly one cache entry — would require reaching
/// into <c>TableGateway&lt;,&gt;</c>'s private <c>_templatesByDialect</c> field via reflection
/// (it is intentionally not exposed, per this codebase's "hide implementation details" policy)
/// plus a THIRD real container just to get a same-version pair. That is exactly the kind of
/// bespoke, reflection-dependent plumbing disproportionate to the value already delivered by the
/// deterministic fakeDb unit test, so it is intentionally not attempted here — see
/// docs/FUTURE_WORK.md for the full recommendation.
/// </para>
/// </summary>
public class MultiTenantDialectVersionTests : IAsyncLifetime
{
    // Straddles the MySqlDialect.UpsertAliasVersionThreshold = new Version(8, 0, 20):
    // 8.0.19 (just below) uses legacy VALUES(col); 8.0.33 (well above) uses incoming.col.
    private const string OldImage = "mysql:8.0.19";
    private const string NewImage = "mysql:8.0.33";

    private readonly ITestOutputHelper _output;
    private IContainer? _oldContainer;
    private IContainer? _newContainer;
    private string? _oldConnectionString;
    private string? _newConnectionString;

    public MultiTenantDialectVersionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.MySql))
        {
            // Skipped in Fact body via Skip.If below; nothing to start.
            return;
        }

        _oldContainer = BuildContainer(OldImage, "rootpassword", "testdb");
        _newContainer = BuildContainer(NewImage, "rootpassword", "testdb");

        await Task.WhenAll(_oldContainer.StartAsync(), _newContainer.StartAsync());

        _oldConnectionString = BuildConnectionString(_oldContainer, "rootpassword", "testdb");
        _newConnectionString = BuildConnectionString(_newContainer, "rootpassword", "testdb");

        await Task.WhenAll(
            WaitForReadyAsync(_oldConnectionString, _output, OldImage),
            WaitForReadyAsync(_newConnectionString, _output, NewImage));
    }

    public async Task DisposeAsync()
    {
        if (_oldContainer != null)
        {
            await _oldContainer.DisposeAsync();
        }

        if (_newContainer != null)
        {
            await _newContainer.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task SharedGateway_TwoRealMySqlVersions_GeneratesCorrectSqlPerTenant()
    {
        Skip.IfNot(IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.MySql),
            "MySQL is not enabled for this test run.");
        Skip.If(_oldConnectionString == null || _newConnectionString == null,
            "MySQL containers failed to initialize.");

        var typeMap = new TypeMapRegistry();
        typeMap.Register<TenantEntity>();

        await using var oldContext = new DatabaseContext(_oldConnectionString!, MySqlClientFactory.Instance, typeMap);
        await using var newContext = new DatabaseContext(_newConnectionString!, MySqlClientFactory.Instance, typeMap);

        await CreateTableAsync(oldContext);
        await CreateTableAsync(newContext);

        // Force version detection on both real servers before inspecting the dialect.
        _ = oldContext.Product;
        _ = newContext.Product;

        var oldVersion = oldContext.GetDialect().ProductInfo.ParsedVersion;
        var newVersion = newContext.GetDialect().ProductInfo.ParsedVersion;
        _output.WriteLine($"Old server ParsedVersion: {oldVersion}");
        _output.WriteLine($"New server ParsedVersion: {newVersion}");

        Assert.True(string.IsNullOrEmpty(oldContext.GetDialect().UpsertIncomingAlias),
            $"MySQL {OldImage} (< 8.0.20) is expected to use the legacy VALUES(col) upsert syntax (no incoming alias).");
        Assert.Equal("incoming", newContext.GetDialect().UpsertIncomingAlias);

        // ONE shared gateway instance, used against both tenants — exactly the multitenancy
        // pattern documented in CLAUDE.md (gateway.Method(entity, tenantCtx)).
        var gateway = new TableGateway<TenantEntity, long>(oldContext);

        var oldEntity = new TenantEntity { Id = 1, Name = "old-tenant-row" };
        var newEntity = new TenantEntity { Id = 1, Name = "new-tenant-row" };

        var oldSql = gateway.BuildUpsert(oldEntity, oldContext).Query.ToString();
        var newSql = gateway.BuildUpsert(newEntity, newContext).Query.ToString();

        _output.WriteLine($"Old-tenant SQL: {oldSql}");
        _output.WriteLine($"New-tenant SQL: {newSql}");

        Assert.Contains("VALUES(", oldSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"incoming\"", oldSql, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("\"incoming\"", newSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VALUES(\"name\")", newSql, StringComparison.OrdinalIgnoreCase);

        // Both tenants' generated SQL must actually execute successfully against their own real
        // server — not just look right as text.
        var oldRows = await gateway.UpsertAsync(oldEntity, oldContext);
        var newRows = await gateway.UpsertAsync(newEntity, newContext);
        Assert.Equal(1, oldRows);
        Assert.Equal(1, newRows);

        var oldRetrieved = await gateway.RetrieveOneAsync(1L, oldContext);
        var newRetrieved = await gateway.RetrieveOneAsync(1L, newContext);
        Assert.Equal("old-tenant-row", oldRetrieved?.Name);
        Assert.Equal("new-tenant-row", newRetrieved?.Name);
    }

    private static async Task CreateTableAsync(IDatabaseContext context)
    {
        await using var container = context.CreateSqlContainer(@"
CREATE TABLE IF NOT EXISTS `tenant_entity` (
    `id` BIGINT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL
)");
        await container.ExecuteNonQueryAsync();
    }

    private static IContainer BuildContainer(string image, string password, string database)
    {
        return new ContainerBuilder()
            .WithImage(image)
            .WithEnvironment("MYSQL_ROOT_PASSWORD", password)
            .WithEnvironment("MYSQL_DATABASE", database)
            .WithPortBinding(3306, true)
            .WithExposedPort(3306)
            .Build();
    }

    private static string BuildConnectionString(IContainer container, string password, string database)
    {
        var hostPort = container.GetMappedPublicPort(3306);
        return $"Server=localhost;Port={hostPort};Database={database};User=root;Password={password};";
    }

    private static async Task WaitForReadyAsync(string connectionString, ITestOutputHelper output, string label)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                output.WriteLine($"{label}: ready");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException($"{label} did not become ready in time.", last);
    }

    [Table("tenant_entity")]
    private class TenantEntity
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
