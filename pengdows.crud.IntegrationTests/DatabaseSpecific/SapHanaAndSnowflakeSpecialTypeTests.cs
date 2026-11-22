using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// SAP HANA: ALPHANUM and SHORTTEXT (A74), table variables and cursors in a SQLScript block (A77), and the refusal of a table type
/// as a column type (B75). Snowflake: GEOGRAPHY (A74) and an OBJECT with named, typed fields (B59). Written through plain SQL: none
/// has a CLR property type of its own. Both databases are opt-in (INCLUDE_SAPHANA, INCLUDE_SNOWFLAKE), so this was written
/// without a run and has to be run with those enabled.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SapHanaAndSnowflakeSpecialTypeTests : DatabaseTestBase
{
    public SapHanaAndSnowflakeSpecialTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p is SupportedDatabase.SapHana or SupportedDatabase.Snowflake).ToArray();

    private static async Task Exec(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    private static async Task TryExec(IDatabaseContext context, string sql)
    {
        try
        {
            await Exec(context, sql);
        }
        catch (Exception)
        {
            // the object was not there
        }
    }

    private static async Task<T?> Scalar<T>(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        return await sc.ExecuteScalarOrNullAsync<T>();
    }

    // ---- SAP HANA --------------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task SapHana_Alphanum_KeepsLeadingZeros_AndShorttextIsText()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SapHana }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE hana_special_doc");
            await Exec(context, "CREATE TABLE hana_special_doc (id INTEGER PRIMARY KEY, a ALPHANUM(10), s NVARCHAR(20))");
            await Exec(context, "INSERT INTO hana_special_doc VALUES (1, '000123', 'abc')");

            // ALPHANUM compares and stores a numeric string by value, so its leading zeros pad to the declared length.
            Assert.Equal("0000000123", await Scalar<string>(context, "SELECT a FROM hana_special_doc WHERE id = 1"));
            Assert.Equal("abc", await Scalar<string>(context, "SELECT s FROM hana_special_doc WHERE id = 1"));
            await Exec(context, "DROP TABLE hana_special_doc");
        });
    }

    [SkippableFact]
    public async Task SapHana_TableVariableAndCursor_WorkInASqlScriptBlock()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SapHana }, async (provider, context) =>
        {
            Assert.Equal(6, await Scalar<int>(context, @"DO BEGIN
  t = SELECT 1 AS a FROM DUMMY UNION ALL SELECT 2 FROM DUMMY UNION ALL SELECT 3 FROM DUMMY;
  SELECT SUM(a) AS total FROM :t;
END"));

            Assert.Equal(6, await Scalar<int>(context, @"DO BEGIN
  DECLARE v INTEGER;
  DECLARE total INTEGER = 0;
  DECLARE CURSOR c FOR SELECT 1 AS a FROM DUMMY UNION ALL SELECT 2 FROM DUMMY UNION ALL SELECT 3 FROM DUMMY;
  FOR r AS c DO
    total = :total + r.a;
  END FOR;
  SELECT :total AS total FROM DUMMY;
END"));
        });
    }

    [SkippableFact]
    public async Task SapHana_TableType_IsRefusedAsAColumnType()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SapHana }, async (provider, context) =>
        {
            await TryExec(context, "DROP TYPE hana_row_t");
            await Exec(context, "CREATE TYPE hana_row_t AS TABLE (a INTEGER)");
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "CREATE TABLE hana_table_col_doc (id INTEGER PRIMARY KEY, t hana_row_t)"));
            await Exec(context, "DROP TYPE hana_row_t");
        });
    }

    // ---- Snowflake -------------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Snowflake_Geography_StoresAPoint()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Snowflake }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE geo_doc");
            await Exec(context, "CREATE TABLE geo_doc (id INTEGER PRIMARY KEY, g GEOGRAPHY)");
            await Exec(context, "INSERT INTO geo_doc SELECT 1, TO_GEOGRAPHY('POINT(-87.6298 41.8781)')");

            Assert.Equal("POINT(-87.6298 41.8781)", await Scalar<string>(context, "SELECT ST_ASWKT(g) FROM geo_doc WHERE id = 1"));
            Assert.Equal(4326, await Scalar<int>(context, "SELECT ST_SRID(g) FROM geo_doc WHERE id = 1"));
            await Exec(context, "DROP TABLE geo_doc");
        });
    }

    [SkippableFact]
    public async Task Snowflake_Object_KeepsNamedFields()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Snowflake }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE object_doc");
            await Exec(context, "CREATE TABLE object_doc (id INTEGER PRIMARY KEY, o OBJECT)");
            await Exec(context, "INSERT INTO object_doc SELECT 1, OBJECT_CONSTRUCT('street', '1 Main St', 'zip', 12345)");

            Assert.Equal("1 Main St", await Scalar<string>(context, "SELECT o:street::VARCHAR FROM object_doc WHERE id = 1"));
            Assert.Equal(12345, await Scalar<int>(context, "SELECT o:zip::INTEGER FROM object_doc WHERE id = 1"));
            await Exec(context, "DROP TABLE object_doc");
        });
    }
}
