using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Integration test proving, against a REAL Firebird instance, that
/// <c>PrimaryKeyTableGateway&lt;TEntity&gt;.UpsertAsync</c> succeeds for an entity that has ONLY
/// <c>[PrimaryKey]</c> columns and no other updateable columns — the "pure junction table" case.
/// <para>
/// <c>ISqlDialect.SupportsPureKeyUpsert</c> is true only for Firebird, whose
/// <c>UPDATE OR INSERT ... MATCHING (...)</c> syntax has no UPDATE/SET-clause requirement. Every
/// other dialect's MERGE/ON CONFLICT/ON DUPLICATE KEY syntax requires at least one non-key column
/// to set, so <see cref="PrimaryKeyTableGateway{TEntity}.BuildUpsert"/> throws
/// <see cref="NotSupportedException"/> for such an entity everywhere else — see
/// <c>PrimaryKeyTableGatewayTests</c> (fakeDb) for that side of the contract. <c>fakeDb</c> never
/// executes real SQL, so it cannot prove Firebird's <c>UPDATE OR INSERT MATCHING</c> genuinely
/// accepts a statement with an empty column list beyond the MATCHING key — this test proves it
/// against a real server.
/// </para>
/// </summary>
[Collection("IntegrationTests")]
public class FirebirdPureKeyUpsertTests : DatabaseTestBase
{
    private const string TableName = "pure_junction_fb";

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(p => p == SupportedDatabase.Firebird).ToList();
    }

    public FirebirdPureKeyUpsertTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<PureJunctionEntity>();
        await DropTableIfExistsAsync(context, TableName);
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        var leftColumn = context.WrapObjectName("left_id");
        var rightColumn = context.WrapObjectName("right_id");
        await using var container = context.CreateSqlContainer($@"
CREATE TABLE {table} (
    {leftColumn} BIGINT NOT NULL,
    {rightColumn} BIGINT NOT NULL,
    PRIMARY KEY ({leftColumn}, {rightColumn})
)");
        await container.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public Task UpsertAsync_PureKeyEntity_Firebird_Succeeds()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            Assert.True(context.Dialect.SupportsPureKeyUpsert,
                "This test targets Firebird's SupportsPureKeyUpsert=true capability.");

            var gateway = new PrimaryKeyTableGateway<PureJunctionEntity>(context);
            var entity = new PureJunctionEntity { LeftId = 1, RightId = 2 };

            // First call: INSERT branch of UPDATE OR INSERT ... MATCHING.
            var firstCount = await gateway.UpsertAsync(entity, context);
            Assert.Equal(1, firstCount);

            var retrieved = await gateway.RetrieveOneAsync(new PureJunctionEntity { LeftId = 1, RightId = 2 }, context);
            Assert.NotNull(retrieved);
            Assert.Equal(1, retrieved!.LeftId);
            Assert.Equal(2, retrieved.RightId);

            // Second call with the SAME key: UPDATE branch of UPDATE OR INSERT ... MATCHING.
            // With no non-key columns, this degrades to matching-only — must not throw or
            // duplicate the row.
            var secondCount = await gateway.UpsertAsync(entity, context);
            Assert.Equal(1, secondCount);

            var countAfter = await CountRowsAsync(context);
            Assert.Equal(1, countAfter);

            Output.WriteLine($"{provider}: pure-key UpsertAsync succeeded on both insert and update branches");
        });
    }

    private static async Task<int> CountRowsAsync(IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        await using var container = context.CreateSqlContainer($"SELECT COUNT(*) FROM {table}");
        var count = await container.ExecuteScalarOrNullAsync<long>();
        return (int)count;
    }

    [Table(TableName)]
    private class PureJunctionEntity
    {
        [PrimaryKey(1)]
        [Column("left_id", DbType.Int64)]
        public long LeftId { get; set; }

        [PrimaryKey(2)]
        [Column("right_id", DbType.Int64)]
        public long RightId { get; set; }
    }
}
