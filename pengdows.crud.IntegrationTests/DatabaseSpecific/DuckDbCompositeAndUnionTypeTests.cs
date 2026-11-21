using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// DuckDB's STRUCT, MAP and LIST columns come back from DuckDB.NET as dictionaries and a List&lt;T&gt; and reach their natural
/// .NET types through the gateway. Its UNION cannot be read: DuckDB.NET names the type ("Union") and then throws a raw
/// ArgumentException from GetFieldType and GetValue, so the library refuses a result set holding one, by column name, with a
/// typed DataMappingException; the union stays readable through DuckDB's own union_tag and union_extract.
/// </summary>
[Collection("IntegrationTests")]
public sealed class DuckDbCompositeAndUnionTypeTests : DatabaseTestBase
{
    public DuckDbCompositeAndUnionTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.DuckDB];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        // Setup runs once per test on the same database.
        await DropTableIfExistsAsync(context, "duckdb_composites");
        await using var table = context.CreateSqlContainer("""
            CREATE TABLE "duckdb_composites" (
                "id" INTEGER PRIMARY KEY,
                "s" STRUCT(a INTEGER, b VARCHAR),
                "m" MAP(VARCHAR, INTEGER),
                "l" INTEGER[],
                "f" FLOAT[3],
                "u" UNION(num INTEGER, str VARCHAR)
            )
            """);
        await table.ExecuteNonQueryAsync();
        await using var row = context.CreateSqlContainer("""
            INSERT INTO "duckdb_composites"
            VALUES (1, {'a': 1, 'b': 'x'}, MAP {'k1': 1, 'k2': 2}, [1, 2, 3], [1.0, 2.5, 3.0]::FLOAT[3], union_value(str := 'hi'))
            """);
        await row.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task StructMapAndLists_ReadThroughTheGatewayAsTheirNaturalTypes()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.DuckDB, async context =>
        {
            var row = await new TableGateway<DuckDbCompositeRow, int>(context).RetrieveOneAsync(1, context);

            Assert.NotNull(row);
            Assert.Equal(1, row!.S!["a"]);
            Assert.Equal("x", row.S["b"]);
            Assert.Equal(new Dictionary<string, int> { ["k1"] = 1, ["k2"] = 2 }, row.M);
            Assert.Equal(new[] { 1, 2, 3 }, row.L);
            Assert.Equal(new[] { 1f, 2.5f, 3f }, row.F);
        });
    }

    [SkippableFact]
    public async Task UnionColumn_IsRefusedByName_AndTheUnionIsReadableThroughItsFunctions()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.DuckDB, async context =>
        {
            var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
                await new TableGateway<DuckDbUnionRow, int>(context).RetrieveOneAsync(1, context));
            Assert.Contains("'u'", ex.Message);
            Assert.Contains("UNION", ex.Message);
            Assert.Equal(0, context.NumberOfOpenConnections);

            // The scalar path refuses it too.
            await using (var scalar = context.CreateSqlContainer("""SELECT "u" FROM "duckdb_composites" WHERE "id" = 1"""))
            {
                await Assert.ThrowsAsync<DataMappingException>(async () => await scalar.ExecuteScalarRequiredAsync<object>());
            }

            // The context still works, and the union's own functions read it.
            await using var tag = context.CreateSqlContainer("""SELECT union_tag("u") FROM "duckdb_composites" WHERE "id" = 1""");
            Assert.Equal("str", await tag.ExecuteScalarRequiredAsync<string>());
            await using var value = context.CreateSqlContainer("""SELECT union_extract("u", 'str') FROM "duckdb_composites" WHERE "id" = 1""");
            Assert.Equal("hi", await value.ExecuteScalarRequiredAsync<string>());
        });
    }
}

[Table("duckdb_composites")]
internal sealed class DuckDbCompositeRow
{
    [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("s", DbType.Object)] public Dictionary<string, object?>? S { get; set; }
    [Column("m", DbType.Object)] public Dictionary<string, int>? M { get; set; }
    [Column("l", DbType.Object)] public int[]? L { get; set; }
    [Column("f", DbType.Object)] public float[]? F { get; set; }
}

[Table("duckdb_composites")]
internal sealed class DuckDbUnionRow
{
    [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("u", DbType.Object)] public object? U { get; set; }
}
