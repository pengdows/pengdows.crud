using System.Data;
using System.Data.OleDb;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Access column types that OLE DB DDL cannot create (Hyperlink, Attachment, Multi-value). They are
/// created through DAO (<c>DAO.DBEngine.120</c>, shipped with the ACE redistributable), then used
/// through pengdows.crud. Live-verified facts these tests lock in:
/// <list type="bullet">
///   <item>Hyperlink is a MEMO with an attribute: it round-trips through the gateway as text.</item>
///   <item>Attachment and Multi-value fields read as child columns (<c>v.FileName</c>, <c>v.Value</c>)
///     and can only be written with <c>INSERT INTO t (v.X) SELECT ... FROM t WHERE ...</c> against
///     an existing parent row - an INSERT naming the field with another field is rejected.</item>
///   <item>Native Large Number (64-bit) needs a current ACE build; 16.0.5320 refused it by DDL, ADOX and
///     DAO ("Invalid field data type"). See AccessDialect for how a long is bound.</item>
/// </list>
/// Windows-only (ACE + COM); skipped elsewhere.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class AccessComplexTypeTests : IDisposable
{
    private const int DaoHyperlinkMemoAttribute = 0x8000;
    private const int DaoLong = 4;
    private const int DaoMemo = 12;
    private const int DaoAttachment = 101;
    private const int DaoComplexLong = 102;
    private const int DaoComplexText = 109;
    private const int DaoBigInt = 16;

    private readonly ITestOutputHelper _output;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pengdows.complex.{Guid.NewGuid():N}.accdb");

    public AccessComplexTypeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Table("t_hl")]
    private sealed class HyperlinkRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public string V { get; set; } = string.Empty;
    }

    [SkippableFact]
    public async Task Hyperlink_RoundTripsThroughTheGateway()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTables(("t_hl", DaoMemo, hyperlink: true));
        await using var context = CreateContext();

        var gateway = new TableGateway<HyperlinkRow, int>(context);
        var row = new HyperlinkRow { Id = 1, V = "http://example.com/a#anchor" };
        await gateway.CreateAsync(row, context);

        var loaded = await gateway.RetrieveOneAsync(1, context);
        Assert.Equal(row.V, loaded?.V);
    }

    [SkippableFact]
    public async Task Attachment_IsWrittenWithInsertSelectAndReadAsChildColumns()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTables(("t_att", DaoAttachment, hyperlink: false));
        await using var context = CreateContext();

        await Exec(context, "INSERT INTO t_att (id) VALUES (1)");
        await Exec(context, "INSERT INTO t_att (v.FileName) SELECT 'a.txt' FROM t_att WHERE id = 1");

        await using var read = context.CreateSqlContainer("SELECT id, v.FileName AS name FROM t_att");
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, Convert.ToInt32(reader["id"]));
        Assert.Equal("a.txt", reader["name"]);
    }

    [SkippableFact]
    public async Task MultiValue_IsWrittenWithInsertSelectAndReadAsChildColumn()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTables(("t_mv", DaoComplexText, hyperlink: false));
        await using var context = CreateContext();

        await Exec(context, "INSERT INTO t_mv (id) VALUES (1)");
        await Exec(context, "INSERT INTO t_mv (v.Value) SELECT 'x' FROM t_mv WHERE id = 1");

        await using var read = context.CreateSqlContainer("SELECT v.Value AS item FROM t_mv WHERE id = 1");
        Assert.Equal("x", await read.ExecuteScalarRequiredAsync<string>());
    }

    [SkippableFact]
    public void NativeLargeNumber_CanBeCreatedWithTheInstalledAceProvider()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");

        // Needs an ACE build with Large Number support; the older 16.0.5320 refused it ("Invalid
        // field data type"), the current one accepts it.
        CreateTables(("t_big", DaoBigInt, hyperlink: false));
    }

    private async Task Exec(IDatabaseContext context, string sql)
    {
        await using var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private IDatabaseContext CreateContext() => new DatabaseContext(
        new DatabaseContextConfiguration
        {
            ConnectionString = $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={_path};",
            DbMode = DbMode.Best
        },
        OleDbFactory.Instance,
        null,
        new TypeMapRegistry());

    private void CreateTables(params (string Name, int DaoType, bool hyperlink)[] tables)
    {
        dynamic engine = Activator.CreateInstance(
            Type.GetTypeFromProgID("DAO.DBEngine.120")
            ?? throw new InvalidOperationException(
                "DAO.DBEngine.120 not found - the Access Database Engine Redistributable is not installed."))!;
        dynamic db = engine.CreateDatabase(_path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128);
        try
        {
            foreach (var (name, daoType, hyperlink) in tables)
            {
                dynamic table = db.CreateTableDef(name);
                table.Fields.Append(table.CreateField("id", DaoLong));
                dynamic field = table.CreateField("v", daoType);
                if (hyperlink)
                {
                    field.Attributes = (int)field.Attributes | DaoHyperlinkMemoAttribute;
                }

                table.Fields.Append(field);
                db.TableDefs.Append(table);
            }
        }
        finally
        {
            db.Close();
        }
    }
}
