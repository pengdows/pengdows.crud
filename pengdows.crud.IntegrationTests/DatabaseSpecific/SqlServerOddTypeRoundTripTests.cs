using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

[Collection("IntegrationTests")]
public sealed class SqlServerOddTypeRoundTripTests : DatabaseTestBase
{
    public SqlServerOddTypeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        [SupportedDatabase.SqlServer];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var table = context.CreateSqlContainer(
            "CREATE TABLE [dbo].[rowversion_roundtrip] ([id] INT NOT NULL PRIMARY KEY, [value] NVARCHAR(100) NOT NULL, [version] ROWVERSION NOT NULL)");
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task RowVersion_IsGeneratedAndHydratedAsClrValueObject()
    {
        await RunTestAgainstAllProvidersAsync(async (_, context) =>
        {
            var gateway = new TableGateway<RowVersionEntity, int>(context);
            var entity = new RowVersionEntity { Id = 1, Value = "before" };

            Assert.True(await gateway.CreateAsync(entity, context));
            var first = await gateway.RetrieveOneAsync(entity.Id, context);

            Assert.NotNull(first);
            Assert.Equal(8, first!.Version.ToArray().Length);
            Assert.NotEqual(default, first.Version);

            first.Value = "after";
            Assert.Equal(1, await gateway.UpdateAsync(first, context));
            var second = await gateway.RetrieveOneAsync(entity.Id, context);

            Assert.NotNull(second);
            Assert.NotEqual(first.Version, second!.Version);
        });
    }
}

[Table("rowversion_roundtrip")]
internal sealed class RowVersionEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("value", DbType.String)]
    public string Value { get; set; } = string.Empty;

    [Version]
    [NonInsertable]
    [Column("version", DbType.Binary)]
    public RowVersion Version { get; set; }
}
