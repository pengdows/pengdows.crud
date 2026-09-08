using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Regression coverage for a real bug found running RetryContext against every real database in
/// the testbed: TableGateway's Tier-1 BuildX methods use a per-dialect cached-template fast path
/// (SqlContainer.Clone(context)) that never actually registered with a RetryContext's queue,
/// because Clone() built the clone directly rather than through targetContext.CreateSqlContainer()
/// — so every queued entity command silently never executed, on every provider, identically. A
/// second, related bug: the one-time per-dialect template-priming step (which also creates
/// several throwaway SqlContainers to determine dialect-specific SQL shape) used whichever
/// per-call context happened to trigger the cache miss, so it would ALSO leak spurious commands
/// into a RetryContext's queue the first time a given dialect was used with it.
/// </summary>
public class RetryContextTableGatewayIntegrationTests
{
    [Table("rc_gateway_test")]
    private sealed class GatewayTestEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private static DatabaseContext CreateContext(fakeDbFactory factory)
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
    }

    [Fact]
    public async Task BuildCreate_AgainstRetryContext_QueuesExactlyOneCommandOnAColdDialectCache()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        // A brand-new TableGateway instance guarantees this entity/dialect pair has never been
        // used before, so GetContainerTemplatesForDialect is guaranteed to be a cache miss here.
        var helper = new TableGateway<GatewayTestEntity, long>(ctx);
        await using var rc = new RetryContext(ctx, RetryContextType.Sequential);

        using var container = helper.BuildCreate(new GatewayTestEntity { Id = 1, Name = "a" }, rc);

        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_AfterBuildCreateAgainstRetryContext_ActuallyExecutesTheInsert()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var helper = new TableGateway<GatewayTestEntity, long>(ctx);
        await using var rc = new RetryContext(ctx, RetryContextType.Sequential);

        var conn = new fakeDbConnection();
        factory.Connections.Add(conn);

        using var container = helper.BuildCreate(new GatewayTestEntity { Id = 1, Name = "a" }, rc);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
        Assert.Contains(conn.ExecutedNonQueryTexts, t => t.Contains("INSERT INTO", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildCreate_AgainstRetryContext_TwoEntitiesQueueExactlyTwoCommands()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var helper = new TableGateway<GatewayTestEntity, long>(ctx);
        await using var rc = new RetryContext(ctx, RetryContextType.Sequential);

        using var c1 = helper.BuildCreate(new GatewayTestEntity { Id = 1, Name = "a" }, rc);
        using var c2 = helper.BuildCreate(new GatewayTestEntity { Id = 2, Name = "b" }, rc);

        Assert.Equal(2, rc.QueuedCommandCount);
    }
}
