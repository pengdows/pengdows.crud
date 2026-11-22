using System.Data;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// The PostgreSQL types the fidelity cells have no cell for: case-insensitive text (citext), the built-in 2-D geometric primitives,
/// a user-defined composite, an enum and a domain. Each is written from a text parameter through CAST(... AS type) and read back as
/// text, which is the form every dialect and driver in the PostgreSQL family supports, so a value that survives here is stored as
/// that type and not as a string. citext needs its extension; a server without it fails the test, it does not skip.
/// </summary>
[Collection("IntegrationTests")]
public sealed class PostgreSqlFamilyOddTypeRoundTripTests : DatabaseTestBase
{
    public PostgreSqlFamilyOddTypeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.PostgreSql).ToArray();

    private static async Task Exec(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    private static async Task<string?> RoundTrip(IDatabaseContext context, string table, string column, string type, string input)
    {
        await using (var insert = context.CreateSqlContainer($"INSERT INTO {table} (id, {column}) VALUES (1, CAST("))
        {
            var p = insert.AddParameterWithValue("v", DbType.String, input);
            insert.Query.Append(insert.MakeParameterName(p));
            insert.Query.Append($" AS {type}))");
            await insert.ExecuteNonQueryAsync();
        }

        await using var read = context.CreateSqlContainer($"SELECT CAST({column} AS text) FROM {table} WHERE id = 1");
        return await read.ExecuteScalarOrNullAsync<string>();
    }

    private async Task<string> Fresh(IDatabaseContext context, string name, string column, string type)
    {
        await DropTableIfExistsAsync(context, name);
        var table = IntegrationObjectNameHelper.Table(context, name);
        await Exec(context, $"CREATE TABLE {table} (id INTEGER PRIMARY KEY, {column} {type})");
        return table;
    }

    [SkippableTheory]
    [InlineData("point", "(1.5,-2.25)", "(1.5,-2.25)")]
    [InlineData("line", "{1,-1,0}", "{1,-1,0}")]
    [InlineData("lseg", "[(0,0),(3,4)]", "[(0,0),(3,4)]")]
    [InlineData("box", "(4,3),(0,0)", "(4,3),(0,0)")]
    [InlineData("path", "[(0,0),(1,1),(2,0)]", "[(0,0),(1,1),(2,0)]")]
    [InlineData("polygon", "((0,0),(4,0),(4,3))", "((0,0),(4,0),(4,3))")]
    [InlineData("circle", "<(1,2),3>", "<(1,2),3>")]
    public async Task GeometricPrimitive_IsStoredAsItsType_AndReadsBackAsItsText(string type, string input, string expected)
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            var table = await Fresh(context, "pg_geo_" + type, "g", type);

            Assert.Equal(expected, await RoundTrip(context, table, "g", type, input));

            await using var typeName = context.CreateSqlContainer($"SELECT pg_typeof(g)::text FROM {table} WHERE id = 1");
            Assert.Equal(type, await typeName.ExecuteScalarOrNullAsync<string>());
        });
    }

    [SkippableFact]
    public async Task Citext_ComparesWithoutCase_AndKeepsTheCaseItWasGiven()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            await Exec(context, "CREATE EXTENSION IF NOT EXISTS citext");
            var table = await Fresh(context, "pg_citext", "t", "citext");

            Assert.Equal("MiXeD Case", await RoundTrip(context, table, "t", "citext", "MiXeD Case"));

            await using var find = context.CreateSqlContainer($"SELECT id FROM {table} WHERE t = ");
            var p = find.AddParameterWithValue("v", DbType.String, "mixed case");
            find.Query.Append("CAST(" + find.MakeParameterName(p) + " AS citext)");
            Assert.Equal(1, await find.ExecuteScalarOrNullAsync<int>());

            await using var plain = context.CreateSqlContainer($"SELECT COUNT(*) FROM {table} WHERE CAST(t AS text) = ");
            var q = plain.AddParameterWithValue("v", DbType.String, "mixed case");
            plain.Query.Append(plain.MakeParameterName(q));
            Assert.Equal(0, await plain.ExecuteScalarRequiredAsync<int>()); // as text the case matters again
        });
    }

    [SkippableFact]
    public async Task Composite_RoundTripsItsFields_AsRowText()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            await Exec(context, "DROP TABLE IF EXISTS pg_composite_doc");
            await Exec(context, "DROP TYPE IF EXISTS pg_address_t");
            await Exec(context, "CREATE TYPE pg_address_t AS (street text, zip integer, active boolean)");
            await Exec(context, "CREATE TABLE pg_composite_doc (id INTEGER PRIMARY KEY, a pg_address_t)");

            Assert.Equal("(\"1 Main St\",12345,t)", await RoundTrip(context, "pg_composite_doc", "a", "pg_address_t", "(\"1 Main St\",12345,t)"));

            await using var field = context.CreateSqlContainer("SELECT (a).zip FROM pg_composite_doc WHERE id = 1");
            Assert.Equal(12345, await field.ExecuteScalarRequiredAsync<int>());

            await Exec(context, "DROP TABLE pg_composite_doc");
            await Exec(context, "DROP TYPE pg_address_t");
        });
    }

    [SkippableFact]
    public async Task Enum_StoresLabels_InTheirDeclaredOrder_AndRefusesAnUnknownLabel()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            await Exec(context, "DROP TABLE IF EXISTS pg_enum_doc");
            await Exec(context, "DROP TYPE IF EXISTS pg_mood_t");
            await Exec(context, "CREATE TYPE pg_mood_t AS ENUM ('sad', 'ok', 'happy')");
            await Exec(context, "CREATE TABLE pg_enum_doc (id INTEGER PRIMARY KEY, m pg_mood_t)");

            Assert.Equal("ok", await RoundTrip(context, "pg_enum_doc", "m", "pg_mood_t", "ok"));

            await using var order = context.CreateSqlContainer("SELECT CAST(m > 'sad'::pg_mood_t AS integer) FROM pg_enum_doc WHERE id = 1");
            Assert.Equal(1, await order.ExecuteScalarRequiredAsync<int>()); // ordered by declaration, not alphabetically ('ok' < 'sad')

            await using var bad = context.CreateSqlContainer("INSERT INTO pg_enum_doc (id, m) VALUES (2, CAST(");
            var p = bad.AddParameterWithValue("v", DbType.String, "furious");
            bad.Query.Append(bad.MakeParameterName(p) + " AS pg_mood_t))");
            await Assert.ThrowsAnyAsync<Exception>(async () => await bad.ExecuteNonQueryAsync());

            await Exec(context, "DROP TABLE pg_enum_doc");
            await Exec(context, "DROP TYPE pg_mood_t");
        });
    }

    [SkippableFact]
    public async Task Domain_StoresItsBaseType_AndEnforcesItsCheck()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            await Exec(context, "DROP TABLE IF EXISTS pg_domain_doc");
            await Exec(context, "DROP DOMAIN IF EXISTS pg_percent_d");
            await Exec(context, "CREATE DOMAIN pg_percent_d AS numeric(5,2) CHECK (VALUE BETWEEN 0 AND 100)");
            await Exec(context, "CREATE TABLE pg_domain_doc (id INTEGER PRIMARY KEY, p pg_percent_d)");

            Assert.Equal("99.50", await RoundTrip(context, "pg_domain_doc", "p", "pg_percent_d", "99.5"));

            await using var bad = context.CreateSqlContainer("INSERT INTO pg_domain_doc (id, p) VALUES (2, CAST(");
            var p = bad.AddParameterWithValue("v", DbType.String, "101");
            bad.Query.Append(bad.MakeParameterName(p) + " AS pg_percent_d))");
            await Assert.ThrowsAnyAsync<Exception>(async () => await bad.ExecuteNonQueryAsync());

            await Exec(context, "DROP TABLE pg_domain_doc");
            await Exec(context, "DROP DOMAIN pg_percent_d");
        });
    }
}
