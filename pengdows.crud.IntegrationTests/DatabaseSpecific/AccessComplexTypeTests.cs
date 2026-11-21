using System.Data;
using System.Data.OleDb;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
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
///   <item>A Calculated field is a column whose value Access computes from an expression: it cannot be written, and it reads
///     through the gateway as its result type.</item>
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
    private const int DaoBoolean = 1;
    private const int DaoCurrency = 5;
    private const int DaoDouble = 7;
    private const int DaoDate = 8;
    private const int DaoText = 10;
    private const int DaoGuid = 15;

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

    [Table("t_calc")]
    private sealed class CalculatedRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("a", DbType.Int32)] public int A { get; set; }
        [Column("v", DbType.Int32)] public int V { get; set; }
    }

    [SkippableFact]
    public async Task CalculatedField_IsComputedByAccessAndReadThroughTheGateway()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateCalculatedTable("t_calc", "[a]*2");
        await using var context = CreateContext();

        // The calculated column is never written: the row is inserted without it.
        await Exec(context, "INSERT INTO t_calc (id, a) VALUES (1, 21)");

        var gateway = new TableGateway<CalculatedRow, int>(context);
        var loaded = await gateway.RetrieveOneAsync(1, context);
        Assert.Equal(21, loaded?.A);
        Assert.Equal(42, loaded?.V);

        // A later change to the source column is reflected on the next read.
        await Exec(context, "UPDATE t_calc SET a = 5 WHERE id = 1");
        Assert.Equal(10, (await gateway.RetrieveOneAsync(1, context))?.V);
    }

    [SkippableFact]
    public async Task CalculatedField_RefusesAnExplicitWrite()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateCalculatedTable("t_calc", "[a]*2");
        await using var context = CreateContext();

        // A CreateAsync names every mapped column, so a calculated column makes Access refuse the insert: the failure is
        // typed, not a silent drop of the value.
        var gateway = new TableGateway<CalculatedRow, int>(context);
        await Assert.ThrowsAnyAsync<DatabaseException>(async () =>
            await gateway.CreateAsync(new CalculatedRow { Id = 2, A = 1, V = 99 }, context));
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

    // ---- Multi-value fields: every element type (DAO DataTypeEnum 102-109) -------------------------------------------------

    [SkippableTheory]
    [InlineData(102, "7", (byte)7)]
    [InlineData(103, "300", (short)300)]
    [InlineData(104, "70000", 70000)]
    [InlineData(105, "1.5", 1.5f)]
    [InlineData(106, "2.25", 2.25)]
    // A multi-value Decimal made through DAO has scale 0, so only a whole number fits it (12.5 is stored as 12).
    [InlineData(108, "12", 12)]
    [InlineData(109, "'x'", "x")]
    public async Task MultiValue_EveryElementType_IsWrittenWithInsertSelectAndReadAsChildColumn(
        int daoComplexType, string literal, object expected)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTables(("t_mvx", daoComplexType, hyperlink: false));
        await using var context = CreateContext();

        await Exec(context, "INSERT INTO t_mvx (id) VALUES (1)");
        await Exec(context, $"INSERT INTO t_mvx (v.Value) SELECT {literal} FROM t_mvx WHERE id = 1");

        await using var read = context.CreateSqlContainer("SELECT v.Value AS item FROM t_mvx WHERE id = 1");
        var actual = await read.ExecuteScalarRequiredAsync<object>();
        if (expected is string text)
        {
            Assert.Equal(text, actual);
        }
        else
        {
            Assert.Equal(Convert.ToDecimal(expected), Convert.ToDecimal(actual));
        }
    }

    [SkippableFact]
    public async Task MultiValue_Guid_IsWrittenWithInsertSelectAndReadAsChildColumn()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTables(("t_mvg", 107, hyperlink: false));
        await using var context = CreateContext();
        var expected = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        await Exec(context, "INSERT INTO t_mvg (id) VALUES (1)");
        await Exec(context, "INSERT INTO t_mvg (v.Value) SELECT {guid {" + expected + "}} FROM t_mvg WHERE id = 1");

        await using var read = context.CreateSqlContainer("SELECT v.Value AS item FROM t_mvg WHERE id = 1");
        Assert.Equal(expected, await read.ExecuteScalarRequiredAsync<Guid>());
    }

    // ---- Calculated fields: every result type -------------------------------------------------------------------------------

    [Table("t_calc2")]
    private sealed class CalcAllRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("s", DbType.String)] public string S { get; set; } = string.Empty;
        [Column("d", DbType.DateTime)] public DateTime D { get; set; }
        [Column("c", DbType.Currency)] public decimal C { get; set; }
        [Column("n", DbType.Int32)] public int N { get; set; }
        [Column("x", DbType.Double)] public double X { get; set; }
        [Column("ct", DbType.String)] public string Ct { get; set; } = string.Empty;
        [Column("cd", DbType.DateTime)] public DateTime Cd { get; set; }
        [Column("cc", DbType.Currency)] public decimal Cc { get; set; }
        [Column("cb", DbType.Boolean)] public bool Cb { get; set; }
        [Column("cx", DbType.Double)] public double Cx { get; set; }
    }

    [SkippableFact]
    public async Task CalculatedFields_OfEveryResultType_AreComputedByAccessAndReadThroughTheGateway()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTableWithColumns("t_calc2", null,
            ("id", DaoLong, 0, null), ("s", DaoText, 20, null), ("d", DaoDate, 0, null), ("c", DaoCurrency, 0, null),
            ("n", DaoLong, 0, null), ("x", DaoDouble, 0, null),
            ("ct", DaoText, 40, "[s] & \"!\""), ("cd", DaoDate, 0, "[d]+1"),
            ("cc", DaoCurrency, 0, "[c]*2"), ("cb", DaoBoolean, 0, "[n]>5"), ("cx", DaoDouble, 0, "[x]*1.5"));
        await using var context = CreateContext();

        await Exec(context, "INSERT INTO t_calc2 (id, s, d, c, n, x) VALUES (1, 'ab', #2026-10-05#, 12.5, 10, 2)");
        await Exec(context, "INSERT INTO t_calc2 (id, s, d, c, n, x) VALUES (2, 'cd', #2026-12-31#, 0.25, 3, 4)");

        var gateway = new TableGateway<CalcAllRow, int>(context);
        var one = await gateway.RetrieveOneAsync(1, context);
        var two = await gateway.RetrieveOneAsync(2, context);

        Assert.NotNull(one);
        Assert.Equal("ab!", one!.Ct);
        Assert.Equal(new DateTime(2026, 10, 6), one.Cd);
        Assert.Equal(25m, one.Cc);
        Assert.True(one.Cb);
        Assert.Equal(3.0, one.Cx);
        Assert.NotNull(two);
        Assert.Equal("cd!", two!.Ct);
        Assert.Equal(new DateTime(2027, 1, 1), two.Cd);
        Assert.Equal(0.5m, two.Cc);
        Assert.False(two.Cb);
        Assert.Equal(6.0, two.Cx);
    }

    // ---- A GUID column Access fills itself (the Replication ID AutoNumber is this with a GenGUID() default) -------------------

    [Table("t_repl")]
    private sealed class ReplicationRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("g", DbType.Guid)] public Guid G { get; set; }
    }

    [SkippableFact]
    public async Task GuidColumnWithGenGuidDefault_IsGeneratedByAccessAndReadThroughTheGateway()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        // DAO refuses dbAutoIncrField on a GUID field ("Invalid argument"), so the generated value is a GenGUID() default.
        CreateTableWithColumns("t_repl",
            db => db.TableDefs("t_repl").Fields("g").DefaultValue = "GenGUID()",
            ("id", DaoLong, 0, null), ("g", DaoGuid, 0, null));
        await using var context = CreateContext();

        await Exec(context, "INSERT INTO t_repl (id) VALUES (1)");
        await Exec(context, "INSERT INTO t_repl (id) VALUES (2)");

        var gateway = new TableGateway<ReplicationRow, int>(context);
        var one = await gateway.RetrieveOneAsync(1, context);
        var two = await gateway.RetrieveOneAsync(2, context);
        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotEqual(Guid.Empty, one!.G);
        Assert.NotEqual(Guid.Empty, two!.G);
        Assert.NotEqual(one.G, two.G);
    }

    // ---- Memo variants: rich text (stored as HTML) and append-only -------------------------------------------------------------

    [Table("t_memo")]
    private sealed class MemoRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public string V { get; set; } = string.Empty;
    }

    [SkippableFact]
    public async Task RichTextMemo_RoundTripsItsHtmlThroughTheGateway()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTableWithColumns("t_memo",
            db =>
            {
                // TextFormat (1 = rich text, stored as HTML) is created on first use.
                dynamic field = db.TableDefs("t_memo").Fields("v");
                field.Properties.Append(field.CreateProperty("TextFormat", 2, 1));
            },
            ("id", DaoLong, 0, null), ("v", 12, 0, null));
        await using var context = CreateContext();
        const string text = "<div>héllo <b>wörld</b> ☃ \U0001F600</div>";

        var gateway = new TableGateway<MemoRow, int>(context);
        await gateway.CreateAsync(new MemoRow { Id = 1, V = text }, context);

        Assert.Equal(text, (await gateway.RetrieveOneAsync(1, context))?.V);
    }

    // An append-only memo keeps a history of its text; ACE refuses every SQL write to it, literal or parameter, insert or update
    // ("Object invalid or no longer set"), and an insert that leaves it out works and reads back empty (verified live). So the
    // library cannot store a value in one, and the failure must be a typed exception, never a value silently dropped.
    [SkippableFact]
    public async Task AppendOnlyMemo_CannotBeWrittenThroughSql_AndTheFailureIsTyped()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTableWithColumns("t_memo",
            db =>
            {
                // AppendOnly already exists on every memo field; it is set, not appended.
                dynamic field = db.TableDefs("t_memo").Fields("v");
                field.Properties("AppendOnly").Value = true;
            },
            ("id", DaoLong, 0, null), ("v", 12, 0, null));
        await using var context = CreateContext();

        var gateway = new TableGateway<MemoRow, int>(context);
        await Assert.ThrowsAnyAsync<DatabaseException>(async () =>
            await gateway.CreateAsync(new MemoRow { Id = 1, V = "text" }, context));

        // The row that leaves the memo out is stored, and its memo reads back empty.
        await Exec(context, "INSERT INTO t_memo (id) VALUES (2)");
        Assert.Null(await new TableGateway<NullableMemoRow, int>(context).RetrieveOneAsync(2, context) is { } row ? row.V : "missing");
    }

    [Table("t_memo")]
    private sealed class NullableMemoRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public string? V { get; set; }
    }

    // ---- Limits: the smallest and largest value of each type, through the gateway ----------------------------------------------

    [Table("t_edge")]
    private sealed class EdgeRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("b", DbType.Byte)] public byte B { get; set; }
        [Column("i", DbType.Int16)] public short I { get; set; }
        [Column("l", DbType.Int32)] public int L { get; set; }
        [Column("s", DbType.Single)] public float S { get; set; }
        [Column("d", DbType.Double)] public double D { get; set; }
        [Column("c", DbType.Currency)] public decimal C { get; set; }
        [Column("m", DbType.Decimal)] public decimal M { get; set; }
        [Column("big", DbType.Int64)] public long Big { get; set; }
        [Column("dt", DbType.DateTime)] public DateTime Dt { get; set; }
        [Column("t", DbType.String)] public string T { get; set; } = string.Empty;
        [Column("body", DbType.String)] public string Note { get; set; } = string.Empty;
    }

    [SkippableFact]
    public async Task EveryType_RoundTripsItsSmallestAndLargestValue()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Access requires Windows");
        CreateTables(("t_dummy", DaoLong, hyperlink: false));
        await using var context = CreateContext();
        await Exec(context,
            "CREATE TABLE t_edge (id INTEGER PRIMARY KEY, b BYTE, i SMALLINT, l INTEGER, s SINGLE, d DOUBLE, c CURRENCY, " +
            "m DECIMAL(28,10), big BIGINT, dt DATETIME, t TEXT(255), body MEMO)");

        var smallest = new EdgeRow
        {
            Id = 1, B = byte.MinValue, I = short.MinValue, L = int.MinValue, S = float.MinValue, D = double.MinValue,
            C = -922337203685477.5808m, M = -99999999999999999.9999999999m, Big = long.MinValue,
            Dt = new DateTime(100, 1, 1), T = new string('a', 255), Note = new string('m', 100_000)
        };
        var largest = new EdgeRow
        {
            Id = 2, B = byte.MaxValue, I = short.MaxValue, L = int.MaxValue, S = float.MaxValue, D = double.MaxValue,
            C = 922337203685477.5807m, M = 99999999999999999.9999999999m, Big = long.MaxValue,
            Dt = new DateTime(9999, 12, 31, 23, 59, 59), T = new string('é', 255), Note = new string('☃', 100_000)
        };

        var gateway = new TableGateway<EdgeRow, int>(context);
        var failures = new List<string>();
        foreach (var expected in new[] { smallest, largest })
        {
            try
            {
                await gateway.CreateAsync(expected, context);
                var actual = await gateway.RetrieveOneAsync(expected.Id, context);
                Assert.NotNull(actual);

                void Check(string name, object? want, object? got)
                {
                    if (!Equals(want, got))
                    {
                        failures.Add($"row {expected.Id} {name}: wrote {want}, read {got}");
                    }
                }

                Check("byte", expected.B, actual!.B);
                Check("smallint", expected.I, actual.I);
                Check("integer", expected.L, actual.L);
                Check("single", expected.S, actual.S);
                Check("double", expected.D, actual.D);
                Check("currency", expected.C, actual.C);
                Check("decimal(28,10)", expected.M, actual.M);
                Check("bigint", expected.Big, actual.Big);
                Check("datetime", expected.Dt, actual.Dt);
                Check("text(255)", expected.T, actual.T);
                Check("memo", expected.Note, actual.Note);
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                failures.Add($"row {expected.Id}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        Assert.True(failures.Count == 0, "Limit values that did not round-trip:\n" + string.Join("\n", failures));
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

    private const int DaoCalculatedExpressionType = DaoLong;

    // A Calculated field is a DAO field whose Expression names the computation; it must follow the columns it reads.
    private void CreateCalculatedTable(string name, string expression)
    {
        dynamic engine = Activator.CreateInstance(
            Type.GetTypeFromProgID("DAO.DBEngine.120")
            ?? throw new InvalidOperationException(
                "DAO.DBEngine.120 not found - the Access Database Engine Redistributable is not installed."))!;
        dynamic db = engine.CreateDatabase(_path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128);
        try
        {
            dynamic table = db.CreateTableDef(name);
            table.Fields.Append(table.CreateField("id", DaoLong));
            table.Fields.Append(table.CreateField("a", DaoLong));
            dynamic calculated = table.CreateField("v", DaoCalculatedExpressionType);
            calculated.Expression = expression;
            table.Fields.Append(calculated);
            db.TableDefs.Append(table);
        }
        finally
        {
            db.Close();
        }
    }

    // A DAO table of arbitrary columns (name, DAO type, size, calculated-field expression). A calculated column must follow
    // the columns it reads. afterCreate runs with the open database, for properties DAO only lets a field take once it is in
    // a table.
    private void CreateTableWithColumns(
        string name,
        Action<dynamic>? afterCreate,
        params (string Name, int DaoType, int Size, string? Expression)[] columns)
    {
        dynamic engine = Activator.CreateInstance(
            Type.GetTypeFromProgID("DAO.DBEngine.120")
            ?? throw new InvalidOperationException(
                "DAO.DBEngine.120 not found - the Access Database Engine Redistributable is not installed."))!;
        dynamic db = engine.CreateDatabase(_path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128);
        try
        {
            dynamic table = db.CreateTableDef(name);
            foreach (var (columnName, daoType, size, expression) in columns)
            {
                dynamic field = table.CreateField(columnName, daoType);
                if (size > 0)
                {
                    field.Size = size;
                }

                if (expression != null)
                {
                    field.Expression = expression;
                }

                table.Fields.Append(field);
            }

            db.TableDefs.Append(table);
            afterCreate?.Invoke(db);
        }
        finally
        {
            db.Close();
        }
    }

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
