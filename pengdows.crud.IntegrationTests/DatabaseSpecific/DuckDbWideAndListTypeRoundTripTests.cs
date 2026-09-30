using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-005: DuckDB's HUGEINT/UHUGEINT come back from DuckDB.NET as BigInteger and its LIST as a
/// List&lt;T&gt;. Each reaches its natural .NET type (Int128, UInt128, T[] or List&lt;T&gt;) with
/// no user type hooks, and a HUGEINT key works in a WHERE clause.
/// </summary>
[Collection("IntegrationTests")]
public sealed class DuckDbWideAndListTypeRoundTripTests : DatabaseTestBase
{
    public DuckDbWideAndListTypeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.DuckDB];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var table = context.CreateSqlContainer("""
            CREATE TABLE "duckdb_wide_types" (
                "id" INTEGER PRIMARY KEY,
                "huge" HUGEINT NOT NULL,
                "uhuge" UHUGEINT NOT NULL,
                "int_list" INTEGER[] NOT NULL,
                "text_list" VARCHAR[] NOT NULL
            )
            """);
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task HugeIntUHugeIntAndLists_RoundTripThroughTheGateway()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.DuckDB, async context =>
        {
            var gateway = new TableGateway<DuckDbWideRow, int>(context);
            var rows = new[]
            {
                new DuckDbWideRow { Id = 1, Huge = Int128.MinValue, UHuge = UInt128.MinValue, IntList = [], TextList = [] },
                new DuckDbWideRow { Id = 2, Huge = Int128.MaxValue, UHuge = UInt128.MaxValue, IntList = [1, -2, int.MaxValue], TextList = ["a", "b c"] },
            };
            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context));
            }

            foreach (var expected in rows)
            {
                var actual = await gateway.RetrieveOneAsync(expected.Id, context);
                Assert.NotNull(actual);
                Assert.Equal(expected.Huge, actual!.Huge);
                Assert.Equal(expected.UHuge, actual.UHuge);
                Assert.Equal(expected.IntList, actual.IntList);
                Assert.Equal(expected.TextList, actual.TextList);
            }

            await using var sc = gateway.BuildBaseRetrieve("d", context);
            sc.Query.Append(" WHERE ").Append(sc.WrapObjectName("d.huge")).Append(" = ");
            var p = sc.AddParameterWithValue("h", DbType.Object, Int128.MaxValue);
            sc.Query.Append(sc.MakeParameterName(p));
            var found = await gateway.LoadSingleAsync(sc);
            Assert.Equal(2, found?.Id);
        });
    }
}

[Table("duckdb_wide_types")]
internal sealed class DuckDbWideRow
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("huge", DbType.Object)] public Int128 Huge { get; set; }
    [Column("uhuge", DbType.Object)] public UInt128 UHuge { get; set; }
    [Column("int_list", DbType.Object)] public int[] IntList { get; set; } = [];
    [Column("text_list", DbType.Object)] public List<string> TextList { get; set; } = [];
}
