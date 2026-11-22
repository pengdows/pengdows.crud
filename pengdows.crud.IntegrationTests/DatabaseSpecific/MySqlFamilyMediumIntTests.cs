using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// The 24-bit integer: MEDIUMINT (-8388608..8388607) and MEDIUMINT UNSIGNED (0..16777215), which has no .NET width of its own and
/// is read into an int. Both bounds round-trip through the gateway, and a value one past either bound is refused by the server
/// rather than stored clamped.
/// </summary>
[Collection("IntegrationTests")]
public sealed class MySqlFamilyMediumIntTests : DatabaseTestBase
{
    private const string Name = "medium_int_doc";

    public MySqlFamilyMediumIntTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(DatabaseFamilies.MySql.Contains).ToArray();

    [Table(Name)]
    public sealed class MediumRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("s", DbType.Int32)] public int Signed { get; set; }
        [Column("u", DbType.Int32)] public int Unsigned { get; set; }
    }

    private static async Task<TableGateway<MediumRow, int>> CreateAsync(IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, Name);
        await using (var create = context.CreateSqlContainer(
                         $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, Name)} (id INT PRIMARY KEY, s MEDIUMINT NOT NULL, u MEDIUMINT UNSIGNED NOT NULL)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        return new TableGateway<MediumRow, int>(context);
    }

    [SkippableFact]
    public async Task MediumInt_RoundTripsItsBounds()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = await CreateAsync(context);
            var rows = new[]
            {
                new MediumRow { Id = 1, Signed = -8388608, Unsigned = 0 },
                new MediumRow { Id = 2, Signed = 8388607, Unsigned = 16777215 },
                new MediumRow { Id = 3, Signed = 0, Unsigned = 65536 }
            };
            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context), $"{provider}: insert {row.Id}");
            }

            foreach (var row in rows)
            {
                var back = await gateway.RetrieveOneAsync(row.Id, context);
                Assert.NotNull(back);
                Assert.Equal(row.Signed, back!.Signed);
                Assert.Equal(row.Unsigned, back.Unsigned);
            }
        });
    }

    [SkippableTheory]
    [InlineData(-8388609, 0)]
    [InlineData(8388608, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 16777216)]
    public async Task MediumInt_ValueOutsideItsRange_IsRefused_NotClamped(int signed, int unsigned)
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = await CreateAsync(context);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await gateway.CreateAsync(new MediumRow { Id = 9, Signed = signed, Unsigned = unsigned }, context));

            await using var count = context.CreateSqlContainer($"SELECT COUNT(*) FROM {IntegrationObjectNameHelper.Table(context, Name)}");
            Assert.Equal(0, await count.ExecuteScalarRequiredAsync<int>());
        });
    }
}
