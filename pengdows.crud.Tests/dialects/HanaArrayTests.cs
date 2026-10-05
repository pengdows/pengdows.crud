using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-020, confirmed live (HANA Express 2.00.088, Sap.Data.Hana.Net 2.29): the driver refuses array
/// parameters and HANA converts no text or binary into an ARRAY, but
/// ARRAY(SELECT V FROM JSON_TABLE(?, '$[*]' COLUMNS (O FOR ORDINALITY, V type PATH '$')) ORDER BY O)
/// builds one from a single JSON text parameter (INSERT, UPDATE, both MERGE arms; null elements and
/// empty arrays included). No expression keeps a NULL array NULL (JSON_TABLE over NULL is an empty
/// array, and HANA has no typed NULL ARRAY literal), so a null value has its expression replaced by
/// NULL when the command is prepared. Reads return the column in HANA's wire encoding: an int32
/// count, then per element a null indicator and a little-endian value (INT/BIGINT/SMALLINT), the raw
/// value with all-0xFF for NULL (DOUBLE/REAL), or a length-prefixed CESU-8 string (0xFF for NULL).
/// </summary>
public sealed class HanaArrayTests
{
    [Table("hana_arrays")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("ints", DbType.Object)] public int[]? Ints { get; set; }
        [Column("longs", DbType.Object)] public long?[]? Longs { get; set; }
        [Column("texts", DbType.Object)] public string?[]? Texts { get; set; }
        [Column("dbls", DbType.Object)] public double?[]? Dbls { get; set; }
        [Column("shorts", DbType.Object)] public short[]? Shorts { get; set; }
        [Column("flts", DbType.Object)] public float[]? Flts { get; set; }
        [Column("bin", DbType.Binary)] public byte[]? Bin { get; set; }
    }

    private const string IntArray = "ARRAY(SELECT V FROM JSON_TABLE(?, '$[*]' COLUMNS (O FOR ORDINALITY, V INT PATH '$')) ORDER BY O)";

    private static (DatabaseContext Context, fakeDbConnection Exec) Hana(IEnumerable<Dictionary<string, object?>>? rows = null)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SapHana);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.SapHana });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SapHana };
        if (rows != null)
        {
            exec.EnqueueReaderResult(rows);
        }

        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;EmulatedProduct=SapHana",
            DbMode = DbMode.Standard
        }, factory), exec);
    }

    private static Row Sample() => new()
    {
        Id = 1, Ints = new[] { 1, 2, 3 }, Longs = new long?[] { 5, null }, Texts = new[] { "a\"b", null, "" },
        Dbls = new double?[] { 1.5, null }, Shorts = new short[] { 7 }, Flts = new[] { 0.5f }, Bin = new byte[] { 1 }
    };

    [Fact]
    public async Task Create_BuildsEachArrayFromJsonText()
    {
        var (context, exec) = Hana();
        await using var _ = context;

        await new TableGateway<Row, int>(context).CreateAsync(Sample());

        var insert = exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal));
        Assert.Contains(IntArray, insert.CommandText);
        Assert.Contains("V BIGINT PATH", insert.CommandText);
        Assert.Contains("V NVARCHAR(5000) PATH", insert.CommandText);
        Assert.Contains("V DOUBLE PATH", insert.CommandText);
        Assert.Equal(6, insert.CommandText.Split("JSON_TABLE(").Length - 1);
        var values = insert.Parameters.Select(p => p.Value).ToList();
        Assert.Contains("[1,2,3]", values);
        Assert.Contains("[5,null]", values);
        Assert.Contains("[\"a\\u0022b\",null,\"\"]", values);
        Assert.Contains("[1.5,null]", values);
        Assert.Contains("[7]", values);
        Assert.Contains("[0.5]", values);
        Assert.Contains(values, v => v is byte[]);
    }

    [Fact]
    public async Task Create_NullArray_IsWrittenAsNull_EmptyArrayStaysEmpty()
    {
        var (context, exec) = Hana();
        await using var _ = context;
        var row = Sample();
        row.Ints = null;
        row.Longs = Array.Empty<long?>();

        await new TableGateway<Row, int>(context).CreateAsync(row);

        var insert = exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal));
        Assert.Equal(5, insert.CommandText.Split("JSON_TABLE(").Length - 1);
        Assert.Equal(insert.CommandText.Count(ch => ch == '?'), insert.Parameters.Count);
        Assert.Equal(7, insert.Parameters.Count);
        Assert.Contains(insert.Parameters, p => Equals(p.Value, "[]"));
    }

    [Fact]
    public async Task Upsert_SourceBuildsTheArray()
    {
        var (context, exec) = Hana();
        await using var _ = context;

        await new TableGateway<Row, int>(context).UpsertAsync(Sample());

        var merge = exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("MERGE", StringComparison.Ordinal)).CommandText;
        Assert.Contains(IntArray + " AS \"ints\"", merge);
    }

    // count 3; 01 + int32 per element, 00 for NULL (live: INT ARRAY [1,NULL,3]).
    private static readonly byte[] Ints = Convert.FromHexString("030000000101000000000103000000");

    // count 3; 01 + int64 (live: BIGINT ARRAY [1,-2,long.MaxValue]).
    private static readonly byte[] Longs = Convert.FromHexString("0300000001010000000000000001FEFFFFFFFFFFFFFF01FFFFFFFFFFFFFF7F");

    // count 5: "a", NULL (FF), "" (00), 😀é in CESU-8 (8 bytes), 300 x 'x' (F6 + uint16 length) (live).
    private static readonly byte[] Texts = Convert.FromHexString(
        "050000000161FF0008EDA0BDEDB880C3A9F62C01" + string.Concat(Enumerable.Repeat("78", 300)));

    // count 3: 1.5, -0.25, NULL as all-FF (live: DOUBLE ARRAY).
    private static readonly byte[] Dbls = Convert.FromHexString("03000000000000000000F83F000000000000D0BFFFFFFFFFFFFFFFFF");

    [Fact]
    public async Task Retrieve_DecodesTheWireEncoding()
    {
        var (context, _) = Hana(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = 1, ["ints"] = Convert.FromHexString("0200000001010000000102000000"), ["longs"] = Longs,
                ["texts"] = Texts, ["dbls"] = Dbls, ["shorts"] = Convert.FromHexString("01000000010700"),
                ["flts"] = Convert.FromHexString("010000000000003F"), ["bin"] = new byte[] { 9 }
            }
        });
        await using var _c = context;

        var row = (await new TableGateway<Row, int>(context).RetrieveOneAsync(1))!;

        Assert.Equal(new[] { 1, 2 }, row.Ints);
        Assert.Equal(new long?[] { 1, -2, long.MaxValue }, row.Longs);
        Assert.Equal(new[] { "a", null, "", "😀é", new string('x', 300) }, row.Texts);
        Assert.Equal(new double?[] { 1.5, -0.25, null }, row.Dbls);
        Assert.Equal(new short[] { 7 }, row.Shorts);
        Assert.Equal(new[] { 0.5f }, row.Flts);
        Assert.Equal(new byte[] { 9 }, row.Bin);
    }

    [Fact]
    public async Task Retrieve_NullElementIntoNonNullableArray_Fails()
    {
        var (context, _) = Hana(new[] { new Dictionary<string, object?> { ["id"] = 1, ["ints"] = Ints } });
        await using var _c = context;

        await Assert.ThrowsAsync<pengdows.crud.exceptions.DataMappingException>(async () =>
            await new TableGateway<Row, int>(context).RetrieveOneAsync(1));
    }

    // An int[] property over a BIGINT ARRAY column: the widths don't match, which fails rather than
    // misreading.
    [Fact]
    public async Task Retrieve_ElementWidthMismatch_Fails()
    {
        var (context, _) = Hana(new[] { new Dictionary<string, object?> { ["id"] = 1, ["ints"] = Longs } });
        await using var _c = context;

        await Assert.ThrowsAsync<pengdows.crud.exceptions.DataMappingException>(async () =>
            await new TableGateway<Row, int>(context).RetrieveOneAsync(1));
    }

    [Fact]
    public async Task UserSql_ArrayParameter_IsBoundAsJsonText()
    {
        var (context, exec) = Hana();
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("UPDATE t SET a = ARRAY(SELECT V FROM JSON_TABLE(?, '$[*]' COLUMNS (O FOR ORDINALITY, V INT PATH '$')) ORDER BY O)");
        sc.AddParameterWithValue("p", DbType.Object, new[] { 4, 5 });

        await sc.ExecuteNonQueryAsync();

        Assert.Equal("[4,5]", exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("UPDATE t", StringComparison.Ordinal)).Parameters.Single().Value);
    }

    private static (pengdows.crud.dialects.SqlDialect Dialect, IColumnInfo Ints) HanaParts(DatabaseContext context)
    {
        var info = pengdows.crud.@internal.DatabaseContextTypeMapExtensions.GetInternalTypeMapRegistry(context).GetTableInfo<Row>();
        return ((pengdows.crud.dialects.SqlDialect)context.Dialect, info.Columns["ints"]);
    }

    // A binder that set the array on the parameter itself still gets JSON text.
    [Fact]
    public async Task MarkColumnParameter_ArrayValue_BecomesJsonText()
    {
        var (context, _) = Hana();
        await using var _c = context;
        var (dialect, ints) = HanaParts(context);
        var parameter = new fakeDbParameter { Value = new[] { 1, 2 } };

        dialect.MarkColumnParameter(parameter, ints);

        Assert.Equal("[1,2]", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    // A null array whose expression isn't in the command (hand-written SQL) is left alone.
    [Fact]
    public async Task PrepareCommand_NullArrayWithoutItsExpression_LeavesTheCommand()
    {
        var (context, _) = Hana();
        await using var _c = context;
        var (dialect, ints) = HanaParts(context);
        var parameter = new fakeDbParameter { Value = DBNull.Value };
        dialect.MarkColumnParameter(parameter, ints);
        using var command = new fakeDbCommand { CommandText = "SELECT 'a?' FROM DUMMY" };
        command.Parameters.Add(parameter);

        await dialect.PrepareCommandAsync(command, default);

        Assert.Equal("SELECT 'a?' FROM DUMMY", command.CommandText);
        Assert.Single(command.Parameters);
    }

    // A '?' in a comment isn't a marker: the null array after it is still found and rewritten.
    [Fact]
    public async Task PrepareCommand_NullArray_SkipsMarkersInComments()
    {
        var (context, _) = Hana();
        await using var _c = context;
        var (dialect, ints) = HanaParts(context);
        var parameter = new fakeDbParameter { Value = DBNull.Value };
        dialect.MarkColumnParameter(parameter, ints);
        using var command = new fakeDbCommand
        {
            CommandText = "-- why?\nUPDATE t SET a = " + IntArray
        };
        command.Parameters.Add(parameter);

        await dialect.PrepareCommandAsync(command, default);

        Assert.Equal("-- why?\nUPDATE t SET a = NULL", command.CommandText);
        Assert.Empty(command.Parameters);
    }
}
