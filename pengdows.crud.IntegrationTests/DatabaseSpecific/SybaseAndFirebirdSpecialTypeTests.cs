using System.Data;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Sybase ASE's system-name aliases (sysname, longsysname), its timestamp type and cursors in a batch; Firebird's custom BLOB
/// subtypes. Written through plain SQL: none has a CLR property type of its own.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SybaseAndFirebirdSpecialTypeTests : DatabaseTestBase
{
    public SybaseAndFirebirdSpecialTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p is SupportedDatabase.SybaseASE or SupportedDatabase.Firebird).ToArray();

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

    // ---- Sybase ASE ------------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Sybase_Sysname_AndLongsysname_AreVarcharAliases()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SybaseASE }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE sysname_doc");
            await Exec(context, "CREATE TABLE sysname_doc (id INT NOT NULL PRIMARY KEY, n sysname NOT NULL, l longsysname NOT NULL)");
            await Exec(context, "INSERT INTO sysname_doc VALUES (1, 'dbo.object', 'a longer system name')");

            Assert.Equal("dbo.object", await Scalar<string>(context, "SELECT n FROM sysname_doc WHERE id = 1"));
            Assert.Equal("a longer system name", await Scalar<string>(context, "SELECT l FROM sysname_doc WHERE id = 1"));
            Assert.Equal("varchar", await Scalar<string>(context,
                "SELECT t.name FROM syscolumns c, systypes t WHERE c.id = OBJECT_ID('sysname_doc') AND c.name = 'n' AND t.usertype = 2 AND t.type = c.type"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "INSERT INTO sysname_doc VALUES (2, NULL, 'x')")); // NOT NULL
            await Exec(context, "DROP TABLE sysname_doc");
        });
    }

    [SkippableFact]
    public async Task Sybase_Timestamp_IsGeneratedByTheServer_AndChangesOnUpdate()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SybaseASE }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE ts_doc");
            await Exec(context, "CREATE TABLE ts_doc (id INT NOT NULL PRIMARY KEY, name VARCHAR(20) NOT NULL, ts timestamp)");
            await Exec(context, "INSERT INTO ts_doc (id, name) VALUES (1, 'a')");

            var first = await Scalar<byte[]>(context, "SELECT ts FROM ts_doc WHERE id = 1");
            Assert.NotNull(first);
            Assert.Equal(8, first!.Length);
            await Exec(context, "UPDATE ts_doc SET name = 'b' WHERE id = 1");
            var second = await Scalar<byte[]>(context, "SELECT ts FROM ts_doc WHERE id = 1");
            Assert.NotEqual(first, second);
            await Exec(context, "DROP TABLE ts_doc");
        });
    }

    [SkippableFact]
    public async Task Sybase_CursorInsideAProcedure_WalksARowSet()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SybaseASE }, async (provider, context) =>
        {
            // DECLARE CURSOR must be the only statement of a batch, so the cursor lives in a procedure.
            await TryExec(context, "DROP PROCEDURE syb_cursor_sum");
            await TryExec(context, "DROP TABLE syb_cursor_src");
            await Exec(context, "CREATE TABLE syb_cursor_src (v INT NOT NULL)");
            await Exec(context, "INSERT INTO syb_cursor_src VALUES (1)");
            await Exec(context, "INSERT INTO syb_cursor_src VALUES (2)");
            await Exec(context, "INSERT INTO syb_cursor_src VALUES (3)");
            await Exec(context, @"CREATE PROCEDURE syb_cursor_sum AS
BEGIN
  DECLARE @x INT, @sum INT
  SELECT @sum = 0
  DECLARE c CURSOR FOR SELECT v FROM syb_cursor_src
  OPEN c
  FETCH c INTO @x
  WHILE @@sqlstatus = 0
  BEGIN
    SELECT @sum = @sum + @x
    FETCH c INTO @x
  END
  CLOSE c
  DEALLOCATE CURSOR c
  SELECT @sum
END");
            Assert.Equal(6, await Scalar<int>(context, "EXEC syb_cursor_sum"));
            await Exec(context, "DROP PROCEDURE syb_cursor_sum");
            await Exec(context, "DROP TABLE syb_cursor_src");
        });
    }

    // ---- Firebird --------------------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Firebird_BlobSubtypes_BinaryText_AndUserDefined_KeepTheirSubtype()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Firebird }, async (provider, context) =>
        {
            await TryExec(context, "DROP TABLE BLOB_SUBTYPE_DOC");
            await Exec(context, "CREATE TABLE BLOB_SUBTYPE_DOC (ID INTEGER NOT NULL PRIMARY KEY, B BLOB SUB_TYPE 0, T BLOB SUB_TYPE TEXT, U BLOB SUB_TYPE -7)");
            await using (var insert = context.CreateSqlContainer("INSERT INTO BLOB_SUBTYPE_DOC (ID, B, T, U) VALUES (1, "))
            {
                var b = insert.AddParameterWithValue("b", DbType.Binary, new byte[] { 0, 1, 2, 255 });
                insert.Query.Append(insert.MakeParameterName(b) + ", ");
                var t = insert.AddParameterWithValue("t", DbType.String, "héllo text blob");
                insert.Query.Append(insert.MakeParameterName(t) + ", ");
                var u = insert.AddParameterWithValue("u", DbType.Binary, new byte[] { 9, 8, 7 });
                insert.Query.Append(insert.MakeParameterName(u) + ")");
                await insert.ExecuteNonQueryAsync();
            }

            Assert.Equal(new byte[] { 0, 1, 2, 255 }, await Scalar<byte[]>(context, "SELECT B FROM BLOB_SUBTYPE_DOC WHERE ID = 1"));
            Assert.Equal("héllo text blob", await Scalar<string>(context, "SELECT T FROM BLOB_SUBTYPE_DOC WHERE ID = 1"));
            Assert.Equal(new byte[] { 9, 8, 7 }, await Scalar<byte[]>(context, "SELECT U FROM BLOB_SUBTYPE_DOC WHERE ID = 1"));

            // The subtype is recorded on the column's domain: 0 binary, 1 text, a negative number is user defined.
            Assert.Equal(0, await Scalar<int>(context, SubtypeSql("B")));
            Assert.Equal(1, await Scalar<int>(context, SubtypeSql("T")));
            Assert.Equal(-7, await Scalar<int>(context, SubtypeSql("U")));
            await Exec(context, "DROP TABLE BLOB_SUBTYPE_DOC");
        });
    }

    private static string SubtypeSql(string column) =>
        "SELECT f.RDB$FIELD_SUB_TYPE FROM RDB$RELATION_FIELDS rf JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
        $"WHERE rf.RDB$RELATION_NAME = 'BLOB_SUBTYPE_DOC' AND rf.RDB$FIELD_NAME = '{column}'";
}
