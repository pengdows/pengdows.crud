using System.Data;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// The opaque, pseudo and variable types that are not ordinary column types, on the two databases that have the most of them.
/// SQL Server: sysname (system-name alias), sql_variant, a table variable, a cursor variable, and the refusal of cursor and table as
/// column types. Oracle: an object type, ANYDATA, XMLTYPE, a nested table, and the refusal of SYS_REFCURSOR as a column type.
/// Written through plain SQL: none has a CLR property type of its own, so what is proved is what the server stores and returns.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SqlServerAndOracleSpecialTypeTests : DatabaseTestBase
{
    public SqlServerAndOracleSpecialTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p is SupportedDatabase.SqlServer or SupportedDatabase.Oracle).ToArray();

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

    // ---- SQL Server ------------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task SqlServer_Sysname_IsNvarchar128_NotNull()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SqlServer }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE sysname_doc");
            await Exec(context, "CREATE TABLE sysname_doc (id INT PRIMARY KEY, n sysname)");
            await Exec(context, "INSERT INTO sysname_doc VALUES (1, N'dbo.ünïcode')");

            Assert.Equal("dbo.ünïcode", await Scalar<string>(context, "SELECT n FROM sysname_doc WHERE id = 1"));
            Assert.Equal("nvarchar", await Scalar<string>(context,
                "SELECT t.name FROM sys.columns c JOIN sys.types bt ON bt.user_type_id = c.system_type_id AND bt.user_type_id = bt.system_type_id JOIN sys.types t ON t.user_type_id = bt.user_type_id WHERE c.object_id = OBJECT_ID('sysname_doc') AND c.name = 'n'"));
            Assert.Equal(128, await Scalar<int>(context,
                "SELECT c.max_length / 2 FROM sys.columns c WHERE c.object_id = OBJECT_ID('sysname_doc') AND c.name = 'n'"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "INSERT INTO sysname_doc VALUES (2, NULL)"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "INSERT INTO sysname_doc VALUES (3, REPLICATE(CAST(N'x' AS nvarchar(max)), 129))"));
            await Exec(context, "DROP TABLE sysname_doc");
        });
    }

    [SkippableFact]
    public async Task SqlServer_SqlVariant_KeepsTheBaseTypeOfEachValue()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SqlServer }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE variant_doc");
            await Exec(context, "CREATE TABLE variant_doc (id INT PRIMARY KEY, v sql_variant)");
            // One statement per row: a multi-row VALUES list unifies its column to one type before sql_variant sees it.
            await Exec(context, "INSERT INTO variant_doc VALUES (1, CAST(42 AS int))");
            await Exec(context, "INSERT INTO variant_doc VALUES (2, CAST(N'text' AS nvarchar(10)))");
            await Exec(context, "INSERT INTO variant_doc VALUES (3, CAST(1.5 AS decimal(5,2)))");
            await Exec(context, "INSERT INTO variant_doc VALUES (4, NULL)");

            Assert.Equal("int", await Scalar<string>(context, "SELECT CAST(SQL_VARIANT_PROPERTY(v, 'BaseType') AS varchar(20)) FROM variant_doc WHERE id = 1"));
            Assert.Equal("nvarchar", await Scalar<string>(context, "SELECT CAST(SQL_VARIANT_PROPERTY(v, 'BaseType') AS varchar(20)) FROM variant_doc WHERE id = 2"));
            Assert.Equal("decimal", await Scalar<string>(context, "SELECT CAST(SQL_VARIANT_PROPERTY(v, 'BaseType') AS varchar(20)) FROM variant_doc WHERE id = 3"));
            Assert.Equal(42, await Scalar<int>(context, "SELECT CAST(v AS int) FROM variant_doc WHERE id = 1"));
            Assert.Null(await Scalar<string>(context, "SELECT CAST(v AS nvarchar(10)) FROM variant_doc WHERE id = 4"));
            await Exec(context, "DROP TABLE variant_doc");
        });
    }

    [SkippableFact]
    public async Task SqlServer_TableVariableAndCursorVariable_WorkInABatch()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SqlServer }, async (provider, context) =>
        {
            Assert.Equal(3, await Scalar<int>(context,
                "DECLARE @t TABLE (a INT, b NVARCHAR(10)); INSERT INTO @t VALUES (1, N'x'), (2, N'y'), (3, N'z'); SELECT COUNT(*) FROM @t"));

            Assert.Equal(6, await Scalar<int>(context, @"
DECLARE @c CURSOR, @x INT, @sum INT = 0;
SET @c = CURSOR FOR SELECT v FROM (VALUES (1), (2), (3)) AS q(v);
OPEN @c; FETCH NEXT FROM @c INTO @x;
WHILE @@FETCH_STATUS = 0 BEGIN SET @sum += @x; FETCH NEXT FROM @c INTO @x; END
CLOSE @c; DEALLOCATE @c;
SELECT @sum"));
        });
    }

    [SkippableFact]
    public async Task SqlServer_CursorAndTable_AreRefusedAsColumnTypes()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SqlServer }, async (provider, context) =>
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "CREATE TABLE cursor_col_doc (c cursor)"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "CREATE TABLE table_col_doc (t table (a int))"));
        });
    }

    // ---- Oracle ----------------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Oracle_ObjectType_StoresItsAttributes()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Oracle }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE obj_doc");
            await TryExec(context, "DROP TYPE obj_address_t FORCE");
            await Exec(context, "CREATE TYPE obj_address_t AS OBJECT (street VARCHAR2(40), zip NUMBER(5))");
            await Exec(context, "CREATE TABLE obj_doc (id NUMBER(5) PRIMARY KEY, a obj_address_t)");
            await Exec(context, "INSERT INTO obj_doc VALUES (1, obj_address_t('1 Main St', 12345))");

            Assert.Equal("1 Main St", await Scalar<string>(context, "SELECT d.a.street FROM obj_doc d WHERE d.id = 1"));
            Assert.Equal(12345, await Scalar<int>(context, "SELECT d.a.zip FROM obj_doc d WHERE d.id = 1"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "INSERT INTO obj_doc VALUES (2, obj_address_t('x', 123456))"));
            await Exec(context, "DROP TABLE obj_doc");
            await Exec(context, "DROP TYPE obj_address_t");
        });
    }

    [SkippableFact]
    public async Task Oracle_AnyData_KeepsTheTypeOfTheValueItHolds()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Oracle }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE anydata_doc");
            await Exec(context, "CREATE TABLE anydata_doc (id NUMBER(5) PRIMARY KEY, a SYS.ANYDATA)");
            await Exec(context, "INSERT INTO anydata_doc VALUES (1, SYS.ANYDATA.ConvertNumber(42))");
            await Exec(context, "INSERT INTO anydata_doc VALUES (2, SYS.ANYDATA.ConvertVarchar2('text'))");

            Assert.Equal("SYS.NUMBER", await Scalar<string>(context, "SELECT d.a.GetTypeName() FROM anydata_doc d WHERE d.id = 1"));
            Assert.Equal("SYS.VARCHAR2", await Scalar<string>(context, "SELECT d.a.GetTypeName() FROM anydata_doc d WHERE d.id = 2"));
            Assert.Equal(42, await Scalar<int>(context,
                "SELECT d.a.AccessNumber() FROM anydata_doc d WHERE d.id = 1"));
            await Exec(context, "DROP TABLE anydata_doc");
        });
    }

    [SkippableFact]
    public async Task Oracle_XmlType_RoundTripsADocument()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Oracle }, async (provider, context) =>
        {
            // XMLTYPE defaults to a SecureFile CLOB, which the SYSTEM tablespace of the test image refuses (ORA-43853), so the storage is named.
            await TryExec(context, "DROP TABLE xmltype_doc");
            await Exec(context, "CREATE TABLE xmltype_doc (id NUMBER(5) PRIMARY KEY, x XMLTYPE) XMLTYPE COLUMN x STORE AS BASICFILE CLOB");
            await Exec(context, "INSERT INTO xmltype_doc VALUES (1, XMLTYPE('<r><i>1</i><i>2</i></r>'))");

            Assert.Equal("<r><i>1</i><i>2</i></r>", await Scalar<string>(context, "SELECT d.x.getStringVal() FROM xmltype_doc d WHERE d.id = 1"));
            Assert.Equal("2", await Scalar<string>(context, "SELECT d.x.extract('/r/i[2]/text()').getStringVal() FROM xmltype_doc d WHERE d.id = 1"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "INSERT INTO xmltype_doc VALUES (2, XMLTYPE('<r><i></r>'))"));
            await Exec(context, "DROP TABLE xmltype_doc");
        });
    }

    [SkippableFact]
    public async Task Oracle_NestedTable_StoresACollectionPerRow()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Oracle }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE nested_doc");
            await TryExec(context, "DROP TYPE nested_numbers_t FORCE");
            await Exec(context, "CREATE TYPE nested_numbers_t AS TABLE OF NUMBER");
            await Exec(context, "CREATE TABLE nested_doc (id NUMBER(5) PRIMARY KEY, n nested_numbers_t) NESTED TABLE n STORE AS nested_doc_n");
            await Exec(context, "INSERT INTO nested_doc VALUES (1, nested_numbers_t(10, 20, 30))");

            Assert.Equal(3, await Scalar<int>(context, "SELECT COUNT(*) FROM nested_doc d, TABLE(d.n) t WHERE d.id = 1"));
            Assert.Equal(60, await Scalar<int>(context, "SELECT SUM(t.COLUMN_VALUE) FROM nested_doc d, TABLE(d.n) t WHERE d.id = 1"));
            await Exec(context, "DROP TABLE nested_doc");
            await Exec(context, "DROP TYPE nested_numbers_t");
        });
    }

    [SkippableFact]
    public async Task Oracle_RefCursor_IsRefusedAsAColumnType()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Oracle }, async (provider, context) =>
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "CREATE TABLE refcursor_col_doc (c SYS_REFCURSOR)"));
        });
    }
}
