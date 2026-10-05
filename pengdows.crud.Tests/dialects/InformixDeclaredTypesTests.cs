using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Informix columns written correctly only with their declared type (TYPE-020, WRT-006, TYPE-022),
/// confirmed live (Informix 15, Informix.Net.Core 4.1501): TEXT binds only from an IfxType.Text
/// parameter; BSON is written as ?::JSON::BSON and read as col::JSON; DATETIME HOUR TO FRACTION(n)
/// keeps its fraction only from text cast to that type (a TimeSpan is truncated to seconds); BLOB/CLOB
/// can't take a host variable below ~9,000 bytes on INSERT or at any size on UPDATE, and the driver's
/// locator API fails (empty IfxException, then a native crash), so the value is staged in a session
/// temp table (BYTE/TEXT columns, which do take host variables) and the statement reads it back
/// through "(SELECT b::BLOB FROM pengdows_lob_stage WHERE k = ?)", binding only the integer key.
/// </summary>
public sealed class InformixDeclaredTypesTests
{
    [Table("ifx_types")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("txt", DbType.String)] public string? Txt { get; set; }
        [Column("doc", DbType.String)] public string? Doc { get; set; }
        [Column("frac", DbType.Time)] public TimeSpan Frac { get; set; }
        [Column("secs", DbType.Time)] public TimeSpan Secs { get; set; }
        [Column("blb", DbType.Binary)] public byte[]? Blb { get; set; }
        [Column("clb", DbType.String)] public string? Clb { get; set; }
        [Column("plain", DbType.String)] public string? Plain { get; set; }
        [Column("byt", DbType.Binary)] public byte[]? Byt { get; set; }
    }

    [Table("ifx_pk_types")]
    public sealed class PkRow
    {
        [PrimaryKey(1)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("txt", DbType.String)] public string? Txt { get; set; }
        [Column("byt", DbType.Binary)] public byte[]? Byt { get; set; }
    }

    private static readonly fakeDbColumn[] Declared =
    {
        new("txt", typeof(string), "TEXT"),
        new("doc", typeof(string), "BSON"),
        new("frac", typeof(DateTime), "DATETIME HOUR TO FRACTION(5)"),
        new("secs", typeof(DateTime), "DATETIME HOUR TO SECOND"),
        new("blb", typeof(byte[]), "BLOB"),
        new("clb", typeof(string), "CLOB"),
        new("plain", typeof(string), "VARCHAR"),
        new("byt", typeof(byte[]), "BYTE")
    };

    private static (DatabaseContext Context, fakeDbFactory Factory, fakeDbConnection Exec) Informix()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix) { EmulatesInformixParameterMetadata = true };
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Database=test;Server=ifx;EmulatedProduct=Informix",
            DbMode = DbMode.Standard
        }, factory);
        var probe = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix };
        probe.EnqueueReaderResult(new fakeDbDataReader(Array.Empty<Dictionary<string, object>>()) { Columns = Declared });
        factory.Connections.Add(probe);
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix };
        factory.Connections.Add(exec);
        return (context, factory, exec);
    }

    private static Row Sample() => new()
    {
        Id = 1, Txt = "text value", Doc = "{\"a\":1}", Frac = new TimeSpan(0, 13, 45, 30).Add(TimeSpan.FromTicks(1234567)),
        Secs = new TimeSpan(13, 45, 30), Blb = new byte[] { 1, 2, 3 }, Clb = "clob value", Plain = "plain",
        Byt = new byte[] { 9, 8 }
    };

    private static async Task<CapturedCommand> InsertAsync()
    {
        var (context, _, exec) = Informix();
        await using var _c = context;
        await new TableGateway<Row, int>(context).CreateAsync(Sample());
        return exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("INSERT INTO \"ifx_types\"", StringComparison.Ordinal));
    }

    // Live: INSERT and UPDATE take a BYTE/TEXT host variable (TEXT only as IfxType.Text) but refuse a
    // staged subquery ("A blob data type must be supplied within this context"); MERGE takes one from
    // neither a host variable nor a subquery in its UPDATE SET, only from a subquery in its source.
    [Fact]
    public async Task TextAndByte_BindDirectlyInInsert_TextAsIfxTypeText()
    {
        var insert = await InsertAsync();

        Assert.DoesNotContain("(SELECT c FROM", insert.CommandText);
        Assert.DoesNotContain("(SELECT b FROM", insert.CommandText);
        Assert.Equal(nameof(fakeIfxType.Text), insert.Parameters.Single(p => Equals(p.Value, "text value")).ProviderType);
        Assert.Null(insert.Parameters.Single(p => Equals(p.Value, "plain")).ProviderType);
        Assert.Contains(insert.Parameters, p => p.Value is byte[] { Length: 2 });
    }

    [Fact]
    public async Task Upsert_TextAndByteGoThroughTheMergeSource_BlobStaysDirect()
    {
        var (context, _, exec) = Informix();
        await using var _c = context;

        await new TableGateway<Row, int>(context).UpsertAsync(Sample());

        var merge = exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("MERGE", StringComparison.Ordinal)).CommandText;
        Assert.Contains("(SELECT c FROM pengdows_lob_stage WHERE k = ?) AS \"txt\"", merge);
        Assert.Contains("(SELECT b FROM pengdows_lob_stage WHERE k = ?) AS \"byt\"", merge);
        Assert.Contains("t.\"txt\" = s.\"txt\"", merge);
        Assert.Contains("t.\"byt\" = s.\"byt\"", merge);
        Assert.Contains("t.\"blb\" = (SELECT b::BLOB FROM pengdows_lob_stage WHERE k = ?)", merge);
        var values = merge[merge.LastIndexOf("VALUES (", StringComparison.Ordinal)..];
        Assert.Contains("s.\"txt\"", values);
        Assert.Contains("s.\"byt\"", values);
        Assert.Contains(exec.ExecutedNonQueryCommands, c =>
            c.CommandText == "INSERT INTO pengdows_lob_stage (k, c) VALUES (?, ?)" && Equals(c.Parameters[1].Value, "text value") &&
            c.Parameters[1].ProviderType == nameof(fakeIfxType.Text));
        Assert.Contains(exec.ExecutedNonQueryCommands, c =>
            c.CommandText == "INSERT INTO pengdows_lob_stage (k, b) VALUES (?, ?)" && c.Parameters[1].Value is byte[] { Length: 2 });
    }

    [Fact]
    public async Task PrimaryKeyGateway_Upsert_TextAndByteGoThroughTheMergeSource()
    {
        var (context, _, exec) = Informix();
        await using var _c = context;

        await new PrimaryKeyTableGateway<PkRow>(context).UpsertAsync(new PkRow { Id = 1, Txt = "t", Byt = new byte[] { 1 } });

        var merge = exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("MERGE", StringComparison.Ordinal)).CommandText;
        Assert.Contains("(SELECT c FROM pengdows_lob_stage WHERE k = ?) AS \"txt\"", merge);
        Assert.Contains("(SELECT b FROM pengdows_lob_stage WHERE k = ?) AS \"byt\"", merge);
        Assert.Contains("t.\"byt\" = s.\"byt\"", merge);
        Assert.DoesNotContain("{P}", merge);
    }

    [Fact]
    public async Task Bson_IsWrittenThroughJson_AndReadAsJson()
    {
        var (context, _, _) = Informix();
        await using var _c = context;
        var gateway = new TableGateway<Row, int>(context);
        await gateway.CreateAsync(Sample());

        Assert.Matches(@"(\?|\{P\}i\d+)::JSON::BSON", gateway.BuildCreate(Sample()).Query.ToString());
        Assert.Contains("\"doc\"::JSON AS \"doc\"", gateway.BuildBaseRetrieve("a").Query.ToString().Replace("\"a\".", ""));
    }

    [Fact]
    public async Task HourToFraction_IsWrittenAsExactCastText_HourToSecondIsUnchanged()
    {
        var insert = await InsertAsync();

        Assert.Single(Regex.Matches(insert.CommandText, @"CAST\(\? AS DATETIME HOUR TO FRACTION\(5\)\)"));
        Assert.Contains(insert.Parameters, p => Equals(p.Value, "13:45:30.12345"));
        Assert.Contains(insert.Parameters, p => Equals(p.Value, new TimeSpan(13, 45, 30)));
    }

    [Fact]
    public async Task BlobAndClob_AreStagedAndReadBackByKey()
    {
        var (context, _, exec) = Informix();
        await using var _c = context;
        await new TableGateway<Row, int>(context).CreateAsync(Sample());

        var commands = exec.ExecutedNonQueryCommands.Select(c => c.CommandText).ToList();
        Assert.Contains(commands, c => c.StartsWith("CREATE TEMP TABLE IF NOT EXISTS pengdows_lob_stage", StringComparison.Ordinal));
        var stagedBlob = exec.ExecutedNonQueryCommands.Single(c => c.CommandText == "INSERT INTO pengdows_lob_stage (k, b) VALUES (?, ?)");
        Assert.Equal(new byte[] { 1, 2, 3 }, stagedBlob.Parameters[1].Value);
        var stagedClob = exec.ExecutedNonQueryCommands.Single(c => c.CommandText == "INSERT INTO pengdows_lob_stage (k, c) VALUES (?, ?)");
        Assert.Equal("clob value", stagedClob.Parameters[1].Value);

        var insert = exec.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("INSERT INTO \"ifx_types\"", StringComparison.Ordinal));
        Assert.Contains("(SELECT b::BLOB FROM pengdows_lob_stage WHERE k = ?)", insert.CommandText);
        Assert.Contains("(SELECT c::CLOB FROM pengdows_lob_stage WHERE k = ?)", insert.CommandText);
        Assert.DoesNotContain(insert.Parameters, p => p.Value is byte[] { Length: 3 } || Equals(p.Value, "clob value"));
        Assert.True(commands.IndexOf(stagedBlob.CommandText) < commands.IndexOf(insert.CommandText));
    }

    [Fact]
    public async Task NullBlob_IsNotStaged()
    {
        var (context, _, exec) = Informix();
        await using var _c = context;
        var row = Sample();
        row.Blb = null;
        row.Clb = null;
        row.Txt = null;
        row.Byt = null;

        await new TableGateway<Row, int>(context).CreateAsync(row);

        Assert.DoesNotContain(exec.ExecutedNonQueryCommands, c => c.CommandText.StartsWith("INSERT INTO pengdows_lob_stage", StringComparison.Ordinal));
    }
}
