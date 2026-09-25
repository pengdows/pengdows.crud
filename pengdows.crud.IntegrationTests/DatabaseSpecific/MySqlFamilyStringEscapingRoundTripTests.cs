using System.Data;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// MySql.Data binds parameters by substituting backslash-escaped literals into the command text.
/// With NO_BACKSLASH_ESCAPES in the session sql_mode those backslashes are stored literally (or
/// break the statement), corrupting any string containing a quote or backslash, including JSON
/// text. Confirmed live on MySQL and TiDB via MySql.Data (BP-115).
/// </summary>
[Collection("IntegrationTests")]
public sealed class MySqlFamilyStringEscapingRoundTripTests : DatabaseTestBase
{
    public MySqlFamilyStringEscapingRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    private static readonly SupportedDatabase[] Providers =
    {
        SupportedDatabase.MySql,
        SupportedDatabase.MariaDb,
        SupportedDatabase.TiDb
    };

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(Providers.Contains).ToArray();

    [SkippableTheory]
    [InlineData("O'Brien")]
    [InlineData("back\\slash")]
    [InlineData("{\"name\":\"say \\\"hi\\\"\",\"path\":\"C:\\\\temp\"}")]
    public async Task StringParameter_RoundTripsUnchanged(string value)
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await DropTableIfExistsAsync(context, "escape_probe");
            var table = IntegrationObjectNameHelper.Table(context, "escape_probe");
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

            // JSON column: MySQL validates the text, so a corrupted escape is a hard failure.
            if (provider != SupportedDatabase.TiDb && value.StartsWith('{'))
            {
                await DropTableIfExistsAsync(context, "escape_probe_json");
                var jtable = IntegrationObjectNameHelper.Table(context, "escape_probe_json");
                await using (var create = context.CreateSqlContainer($"CREATE TABLE {jtable} ({v} JSON NOT NULL)"))
                {
                    await create.ExecuteNonQueryAsync();
                }

                await using (var insert = context.CreateSqlContainer($"INSERT INTO {jtable} ({v}) VALUES ("))
                {
                    var p = insert.AddParameterWithValue("v", DbType.String, value);
                    insert.Query.Append(insert.MakeParameterName(p)).Append(")");
                    await insert.ExecuteNonQueryAsync();
                }

                await using var jselect = context.CreateSqlContainer($"SELECT {v} FROM {jtable}");
                var stored = await jselect.ExecuteScalarRequiredAsync<string>();
                Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                    System.Text.Json.Nodes.JsonNode.Parse(value), System.Text.Json.Nodes.JsonNode.Parse(stored)), stored);
            }
        });
    }
}
