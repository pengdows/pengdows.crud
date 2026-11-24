using System.Data;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Db2's double-byte GRAPHIC types (B28), structured type (B59), distinct type and XML (B67), array type in SQL PL (A77) and the
/// refusal of array and cursor as column types (B75). Written through plain SQL: none has a CLR property type of its own.
/// Needs Db2's Linux client (libdb2.so), so it was written on Windows and has to be run on a Linux host.
/// </summary>
[Collection("IntegrationTests")]
public sealed class Db2SpecialTypeTests : DatabaseTestBase
{
    public Db2SpecialTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.Db2).ToArray();

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

    [SkippableFact]
    public async Task Graphic_AndVargraphic_StoreDoubleByteText()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Db2, async context =>
        {
            await TryExec(context, "DROP TABLE db2_graphic_doc");
            await Exec(context, "CREATE TABLE db2_graphic_doc (id INTEGER NOT NULL PRIMARY KEY, g GRAPHIC(4), v VARGRAPHIC(10))");
            await using (var insert = context.CreateSqlContainer("INSERT INTO db2_graphic_doc VALUES (1, "))
            {
                var g = insert.AddParameterWithValue("g", DbType.String, "あいう");
                insert.Query.Append("CAST(" + insert.MakeParameterName(g) + " AS GRAPHIC(4)), ");
                var v = insert.AddParameterWithValue("v", DbType.String, "漢字テスト");
                insert.Query.Append("CAST(" + insert.MakeParameterName(v) + " AS VARGRAPHIC(10)))");
                await insert.ExecuteNonQueryAsync();
            }

            Assert.Equal("漢字テスト", await Scalar<string>(context, "SELECT v FROM db2_graphic_doc WHERE id = 1"));
            Assert.Equal(5, await Scalar<int>(context, "SELECT LENGTH(v) FROM db2_graphic_doc WHERE id = 1")); // characters, not bytes
            Assert.Equal(4, await Scalar<int>(context, "SELECT LENGTH(g) FROM db2_graphic_doc WHERE id = 1")); // fixed width, padded
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context,
                "INSERT INTO db2_graphic_doc VALUES (2, NULL, VARGRAPHIC('12345678901'))"));
            await Exec(context, "DROP TABLE db2_graphic_doc");
        });
    }

    // Verified live (2026-11-23): Db2 assigns a plain DECIMAL literal to a distinct-type column and stores it as the base
    // value, so the base type is accepted without an explicit cast. (Informix refuses it; see InformixSpecialTypeTests.)
    [SkippableFact]
    public async Task DistinctType_HasItsBaseStorage_AssignsFromTheBaseType()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Db2, async context =>
        {
            await TryExec(context, "DROP TABLE db2_distinct_doc");
            await TryExec(context, "DROP DISTINCT TYPE db2_money_t");
            await Exec(context, "CREATE DISTINCT TYPE db2_money_t AS DECIMAL(9,2) WITH COMPARISONS");
            await Exec(context, "CREATE TABLE db2_distinct_doc (id INTEGER NOT NULL PRIMARY KEY, m db2_money_t)");
            await Exec(context, "INSERT INTO db2_distinct_doc VALUES (1, db2_money_t(12.50))");

            Assert.Equal(12.50m, await Scalar<decimal>(context, "SELECT DECIMAL(m) FROM db2_distinct_doc WHERE id = 1"));
            await Exec(context, "INSERT INTO db2_distinct_doc VALUES (2, 7.25)");
            Assert.Equal(7.25m, await Scalar<decimal>(context, "SELECT DECIMAL(m) FROM db2_distinct_doc WHERE id = 2"));
            await Exec(context, "DROP TABLE db2_distinct_doc");
            await Exec(context, "DROP DISTINCT TYPE db2_money_t");
        });
    }

    [SkippableFact]
    public async Task Xml_RoundTripsADocument_AndRefusesMalformedXml()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Db2, async context =>
        {
            await TryExec(context, "DROP TABLE db2_xml_doc");
            await Exec(context, "CREATE TABLE db2_xml_doc (id INTEGER NOT NULL PRIMARY KEY, x XML)");
            await Exec(context, "INSERT INTO db2_xml_doc VALUES (1, XMLPARSE(DOCUMENT '<r><i>1</i><i>2</i></r>' PRESERVE WHITESPACE))");

            Assert.Equal("<r><i>1</i><i>2</i></r>", await Scalar<string>(context, "SELECT XMLSERIALIZE(x AS VARCHAR(100)) FROM db2_xml_doc WHERE id = 1"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "INSERT INTO db2_xml_doc VALUES (2, XMLPARSE(DOCUMENT '<r><i></r>' PRESERVE WHITESPACE))"));
            await Exec(context, "DROP TABLE db2_xml_doc");
        });
    }

    [SkippableFact]
    public async Task StructuredType_StoresItsAttributes()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Db2, async context =>
        {
            await TryExec(context, "DROP TABLE db2_struct_doc");
            await TryExec(context, "DROP TYPE db2_addr_t");
            await Exec(context, "CREATE TYPE db2_addr_t AS (street VARCHAR(40), zip INTEGER) MODE DB2SQL");
            await Exec(context, "CREATE TABLE db2_struct_doc (id INTEGER NOT NULL PRIMARY KEY, a db2_addr_t)");
            await Exec(context, "INSERT INTO db2_struct_doc VALUES (1, db2_addr_t()..street('1 Main St')..zip(12345))");

            Assert.Equal("1 Main St", await Scalar<string>(context, "SELECT a..street FROM db2_struct_doc WHERE id = 1"));
            Assert.Equal(12345, await Scalar<int>(context, "SELECT a..zip FROM db2_struct_doc WHERE id = 1"));
            await Exec(context, "DROP TABLE db2_struct_doc");
            await Exec(context, "DROP TYPE db2_addr_t");
        });
    }

    [SkippableFact]
    public async Task ArrayVariable_WorksInsideSqlPl()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Db2, async context =>
        {
            await TryExec(context, "DROP FUNCTION db2_array_sum");
            await TryExec(context, "DROP TYPE db2_ints_t");
            await Exec(context, "CREATE TYPE db2_ints_t AS INTEGER ARRAY[10]");
            await Exec(context, @"CREATE FUNCTION db2_array_sum() RETURNS INTEGER
BEGIN
  DECLARE a db2_ints_t;
  DECLARE s INTEGER DEFAULT 0;
  DECLARE i INTEGER DEFAULT 1;
  SET a = ARRAY[10, 20, 30];
  WHILE i <= CARDINALITY(a) DO
    SET s = s + a[i];
    SET i = i + 1;
  END WHILE;
  RETURN s;
END");
            Assert.Equal(60, await Scalar<int>(context, "VALUES db2_array_sum()"));
            await Exec(context, "DROP FUNCTION db2_array_sum");
            await Exec(context, "DROP TYPE db2_ints_t");
        });
    }

    [SkippableFact]
    public async Task ArrayAndCursor_AreRefusedAsColumnTypes()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Db2, async context =>
        {
            await TryExec(context, "DROP TYPE db2_ints_col_t");
            await Exec(context, "CREATE TYPE db2_ints_col_t AS INTEGER ARRAY[10]");
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "CREATE TABLE db2_array_col_doc (a db2_ints_col_t)"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "CREATE TABLE db2_cursor_col_doc (c CURSOR)"));
            await Exec(context, "DROP TYPE db2_ints_col_t");
        });
    }
}
