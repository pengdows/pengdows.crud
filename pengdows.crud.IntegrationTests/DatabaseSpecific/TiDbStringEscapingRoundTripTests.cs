using System.Data;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// MySql.Data binds parameters by substituting backslash-escaped literals into the command text.
/// With NO_BACKSLASH_ESCAPES in the TiDB session sql_mode those backslashes are stored literally,
/// corrupting any string containing a quote or backslash (including JSON text). Backport of
/// 3.0 81eef2b's TiDB session-settings fix.
/// </summary>
[Collection("IntegrationTests")]
public sealed class TiDbStringEscapingRoundTripTests : DatabaseTestBase
{
    public TiDbStringEscapingRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.TiDb).ToArray();

    [SkippableTheory]
    [InlineData("O'Brien")]
    [InlineData("back\\slash")]
    [InlineData("{\"name\":\"say \\\"hi\\\"\",\"path\":\"C:\\\\temp\"}")]
    public async Task StringParameter_RoundTripsUnchanged(string value)
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.TiDb, async context =>
        {
            await DropTableIfExistsAsync(context, "tidb_escape_probe");
            var table = IntegrationObjectNameHelper.Table(context, "tidb_escape_probe");
            var v = context.WrapObjectName("v");
            await using (var create = context.CreateSqlContainer($"CREATE TABLE {table} ({v} VARCHAR(400) NOT NULL)"))
            {
                await create.ExecuteNonQueryAsync();
            }

            await using (var insert = context.CreateSqlContainer($"INSERT INTO {table} ({v}) VALUES ("))
            {
                var p = insert.AddParameterWithValue("v", DbType.String, value);
                insert.Query.Append(insert.MakeParameterName(p)).Append(")");
                await insert.ExecuteNonQueryAsync();
            }

            await using var select = context.CreateSqlContainer($"SELECT {v} FROM {table}");
            Assert.Equal(value, await select.ExecuteScalarRequiredAsync<string>());
        });
    }
}
