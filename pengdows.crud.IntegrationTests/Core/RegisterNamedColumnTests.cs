using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Columns named after SQL special registers ("user", "current") must be quoted everywhere the
/// gateways reference them. Ported from the testbed's TestRegisterNamedColumnsThroughGateways
/// when its check battery moved here. CONFIRMED live on Informix: an unqualified "user" in an
/// expression resolves to the session user name, so an unqualified retrieve returned the user
/// name instead of the column and an unqualified DELETE ... WHERE "user" = ? compared the
/// register (and could delete every row). Every database must round-trip these columns and touch
/// only the targeted rows.
/// </summary>
[Collection("IntegrationTests")]
public class RegisterNamedColumnTests : DatabaseTestBase
{
    private const string TableName = "register_columns";

    public RegisterNamedColumnTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, TableName);
        await using var create = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, TableName)} (" +
            $"{context.WrapObjectName("id")} {IntegrationObjectNameHelper.BigIntType(provider)} NOT NULL, " +
            $"{context.WrapObjectName("user")} {IntegrationObjectNameHelper.StringType(provider)} NOT NULL, " +
            $"{context.WrapObjectName("current")} {IntegrationObjectNameHelper.IntType(provider)} NOT NULL, " +
            $"PRIMARY KEY ({context.WrapObjectName("id")}))");
        await create.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task RegisterNamedColumns_RoundTripThroughBothGateways()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<RegisterColumnsRow, long>(context);
            var keyed = new PrimaryKeyTableGateway<RegisterColumnsRow>(context);
            var first = new RegisterColumnsRow { Id = 1, User = "row-a" };
            var second = new RegisterColumnsRow { Id = 2, User = "row-b" };
            await gateway.CreateAsync(first, context);
            await gateway.CreateAsync(second, context);

            var loaded = await gateway.RetrieveOneAsync(1L, context);
            Assert.Equal("row-a", loaded?.User);

            var versionBefore = loaded!.Current;
            loaded.User = "row-a2";
            Assert.Equal(1, await gateway.UpdateAsync(loaded, context));
            var reloaded = await gateway.RetrieveOneAsync(1L, context);
            Assert.Equal("row-a2", reloaded?.User);
            Assert.Equal(versionBefore + 1, reloaded!.Current);

            Assert.Equal(1, await gateway.CountWhereEqualsAsync("user", "row-b", context: context));

            Assert.Equal(1, await keyed.BatchDeleteAsync(new[] { second }, context));
            Assert.Equal(1, await gateway.CountAllAsync(context));
        });
    }

    [Table(TableName)]
    private class RegisterColumnsRow
    {
        [Id(true)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [PrimaryKey(1)]
        [Column("user", DbType.String)]
        public string User { get; set; } = string.Empty;

        [Version]
        [Column("current", DbType.Int32)]
        public int Current { get; set; }
    }
}
