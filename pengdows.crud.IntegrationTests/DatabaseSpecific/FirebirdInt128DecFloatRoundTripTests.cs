using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-005: FirebirdClient reads INT128 as BigInteger and DECFLOAT as its own FbDecFloat. They
/// reach Int128 and decimal exactly with no user type hooks. A DECFLOAT column is declared
/// DbType.VarNumeric: FirebirdClient binds DECFLOAT only from FbDecFloat, and NUMERIC never from it.
/// </summary>
[Collection("IntegrationTests")]
public sealed class FirebirdInt128DecFloatRoundTripTests : DatabaseTestBase
{
    public FirebirdInt128DecFloatRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.Firebird];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var table = context.CreateSqlContainer("""
            CREATE TABLE "fb_int128_decfloat" (
                "id" INTEGER NOT NULL PRIMARY KEY,
                "wide" INT128 NOT NULL,
                "decf" DECFLOAT(34) NOT NULL
            )
            """);
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task Int128AndDecFloat_RoundTripThroughTheGateway()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Firebird, async context =>
        {
            var gateway = new TableGateway<FirebirdWideRow, int>(context);
            var rows = new[]
            {
                new FirebirdWideRow { Id = 1, Wide = Int128.MinValue, DecF = decimal.MinValue },
                new FirebirdWideRow { Id = 2, Wide = Int128.MaxValue, DecF = decimal.MaxValue },
                new FirebirdWideRow { Id = 3, Wide = -42, DecF = 12345.6789m },
                new FirebirdWideRow { Id = 4, Wide = 0, DecF = 100m },
            };
            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context));
            }

            foreach (var expected in rows)
            {
                var actual = await gateway.RetrieveOneAsync(expected.Id, context);
                Assert.NotNull(actual);
                Assert.Equal(expected.Wide, actual!.Wide);
                Assert.Equal(expected.DecF, actual.DecF);
            }
        });
    }
}

[Table("fb_int128_decfloat")]
internal sealed class FirebirdWideRow
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("wide", DbType.Object)] public Int128 Wide { get; set; }
    [Column("decf", DbType.VarNumeric)] public decimal DecF { get; set; }
}
