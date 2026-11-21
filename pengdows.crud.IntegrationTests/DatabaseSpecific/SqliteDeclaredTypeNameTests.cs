using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// SQLite has no point, geometry, vector, variant or ROWID column type: the name in a column definition is a label (the
/// fidelity cells declare POINT, GEOMETRY, VECTOR(3), VARIANT, JSON and ROWID columns, which pass because SQLite accepts any name),
/// and a value is stored as the type it is given (manifest typing). SpatiaLite and sqlite-vec, which do add such types, are
/// extensions and are not part of the bundled SQLite. JSONB is built in from SQLite 3.45. These tests pin that, so a fidelity cell
/// that passes on SQLite for one of these types is known to be proving a text or blob round trip, not a native type.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SqliteDeclaredTypeNameTests : DatabaseTestBase
{
    public SqliteDeclaredTypeNameTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.Sqlite];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, "declared_name_probe");
        await using var table = context.CreateSqlContainer(
            "CREATE TABLE declared_name_probe (id INTEGER PRIMARY KEY, p POINT, g GEOMETRY, v VECTOR(3), u VARIANT, j JSON, jb JSONB)");
        await table.ExecuteNonQueryAsync();
        await using var row = context.CreateSqlContainer(
            "INSERT INTO declared_name_probe VALUES (1, 'POINT (1 2)', 'POINT (1 2)', '[1,2,3]', 'text', '{\"a\":1}', x'01')");
        await row.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(IDatabaseContext context, string sql)
    {
        await using var container = context.CreateSqlContainer(sql);
        return await container.ExecuteScalarRequiredAsync<string>();
    }

    [SkippableFact]
    public async Task ExtensionTypeNames_AreLabels_AndValuesAreStoredAsGiven()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Sqlite, async context =>
        {
            // The declared names are kept as written, whatever they are.
            Assert.Equal("POINT,GEOMETRY,VECTOR(3),VARIANT,JSON,JSONB",
                await ScalarAsync(context,
                    "SELECT group_concat(type, ',') FROM (SELECT type FROM pragma_table_info('declared_name_probe') WHERE name <> 'id' ORDER BY cid)"));

            // A value keeps the type it was given: text stays text, a blob stays a blob, under every name.
            Assert.Equal("text,text,text,text,text,blob",
                await ScalarAsync(context,
                    "SELECT typeof(p) || ',' || typeof(g) || ',' || typeof(v) || ',' || typeof(u) || ',' || typeof(j) || ',' || typeof(jb) " +
                    "FROM declared_name_probe WHERE id = 1"));
        });
    }

    [SkippableFact]
    public async Task Jsonb_IsBuiltInFromSqlite345_AndIsABlob()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Sqlite, async context =>
        {
            var version = Version.Parse(await ScalarAsync(context, "SELECT sqlite_version()"));
            Assert.True(version >= new Version(3, 45), $"JSONB needs SQLite 3.45; this is {version}");

            Assert.Equal("blob", await ScalarAsync(context, "SELECT typeof(jsonb('{\"a\":1}'))"));
            Assert.Equal("{\"a\":1}", await ScalarAsync(context, "SELECT json(jsonb('{\"a\":1}'))"));
        });
    }

    [SkippableFact]
    public async Task Rowid_IsAPseudoColumn_ThatIsTheIntegerPrimaryKey()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Sqlite, async context =>
        {
            Assert.Equal("1", await ScalarAsync(context, "SELECT CAST(rowid = id AS TEXT) FROM declared_name_probe WHERE id = 1"));
            Assert.Equal("integer", await ScalarAsync(context, "SELECT typeof(rowid) FROM declared_name_probe WHERE id = 1"));
        });
    }
}
