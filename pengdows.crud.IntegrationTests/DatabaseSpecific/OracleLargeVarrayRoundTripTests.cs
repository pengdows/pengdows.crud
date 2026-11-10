using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

// A VARRAY whose JSON is larger than 4000 bytes. The collection read used the default VARCHAR2 aggregate, which the server
// refuses past 4000 bytes (ORA-40478, confirmed live); RETURNING CLOB reads the whole array. This proves the library's
// read and write of a long collection live, not only the SQL it builds.
[Collection("IntegrationTests")]
public sealed class OracleLargeVarrayRoundTripTests : DatabaseTestBase
{
    public OracleLargeVarrayRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        [SupportedDatabase.Oracle];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var type = context.CreateSqlContainer(
            "CREATE OR REPLACE TYPE large_varray_arr AS VARRAY(2000) OF NUMBER(10)");
        await type.ExecuteNonQueryAsync();
        await using var table = context.CreateSqlContainer("""
            CREATE TABLE "large_varray" (
                "id" NUMBER(10) PRIMARY KEY,
                "items" large_varray_arr
            ) TABLESPACE USERS
            """);
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task AnArrayLargerThan4000BytesOfJsonRoundTripsThroughTheDatabase()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async context =>
        {
            var gateway = new TableGateway<LargeVarrayEntity, int>(context);
            // 1000 values of nine digits: about 11000 bytes of JSON, well past the VARCHAR2 limit.
            var items = Enumerable.Range(1, 1000).Select(i => 100_000_000 + i * 7).ToArray();

            Assert.True(await gateway.CreateAsync(new LargeVarrayEntity { Id = 1, Items = items }, context));

            var row = await gateway.RetrieveOneAsync(1, context);
            Assert.NotNull(row);
            Assert.Equal(items, row!.Items);
        });
    }
}

[Table("large_varray")]
internal sealed class LargeVarrayEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("items", DbType.Object, NativeType = "large_varray_arr")]
    public int[]? Items { get; set; }
}
