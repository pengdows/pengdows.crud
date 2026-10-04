using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DEC-012 (WRT-009, a TiDB defect probed live on v8.5.7): VALUES(col) in ON DUPLICATE KEY UPDATE
/// returns a BIT(64) value byte-reversed. BIT(64) maps to ulong/DbType.UInt64, which BIGINT UNSIGNED
/// shares, so every UInt64 column is treated alike: a single-row upsert sets it from its own bound
/// parameter (correct for both types), and a batch upsert, which can only use VALUES() on TiDB (no
/// row alias), runs one statement per row instead.
/// </summary>
public class TiDbBit64UpsertTests
{
    [Table("bits")]
    public class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("flags", DbType.UInt64)] public ulong Flags { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Table("bits_pk")]
    public class PkRow
    {
        [PrimaryKey(1)] [Column("k", DbType.Int32)] public int K { get; set; }
        [Column("flags", DbType.UInt64)] public ulong Flags { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Table("plain")]
    public class PlainRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("n", DbType.Int32)] public int N { get; set; }
        [Column("s", DbType.String)] public string S { get; set; } = "";
    }

    // REV-083 (probed live, TiDB v8.5.7, MySqlConnector 2.6.2 and MySql.Data 9.4.0): VALUES(col) reverses
    // a BIT(64) value whether it is bound as ulong, long or byte[]; pengdows sees only the DbType, so a
    // long or byte[] column could be BIT(64) too.
    [Table("bits_wide")]
    public class WideRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("n", DbType.Int64)] public long N { get; set; }
        [Column("raw", DbType.Binary)] public byte[] Raw { get; set; } = System.Array.Empty<byte>();
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    private static DatabaseContext Context(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

    private static string FlagsParameter(ISqlContainer sc, ulong value)
    {
        var parameters = (System.Collections.Generic.IDictionary<string, System.Data.Common.DbParameter>)typeof(SqlContainer)
            .GetField("_parameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(sc)!;
        return parameters.Values.Single(p => Equals(p.Value, value) || Equals(p.Value, (decimal)value) || Equals(p.Value, (long)value))
            .ParameterName.TrimStart('@');
    }

    [Fact]
    public void TableGateway_SingleRowUpsert_SetsTheUInt64ColumnFromItsParameter()
    {
        using var ctx = Context(SupportedDatabase.TiDb);
        using var sc = new TableGateway<Row, int>(ctx).BuildUpsert(new Row { Id = 1, Flags = 0x8000_0000_0000_0001UL, Name = "a" });
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("VALUES(\"flags\")", sql);
        Assert.Contains("\"flags\" = @" + FlagsParameter(sc, 0x8000_0000_0000_0001UL), sql);
        Assert.Contains("\"name\" = VALUES(\"name\")", sql);
    }

    [Fact]
    public void PrimaryKeyGateway_SingleRowUpsert_SetsTheUInt64ColumnFromItsParameter()
    {
        using var ctx = Context(SupportedDatabase.TiDb);
        using var sc = new PrimaryKeyTableGateway<PkRow>(ctx).BuildUpsert(new PkRow { K = 1, Flags = 7UL, Name = "a" });
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("VALUES(\"flags\")", sql);
        Assert.Contains("\"flags\" = @" + FlagsParameter(sc, 7UL), sql);
    }

    [Fact]
    public void TableGateway_BatchUpsert_WithAUInt64Column_RunsOneStatementPerRow()
    {
        using var ctx = Context(SupportedDatabase.TiDb);
        var batch = new TableGateway<Row, int>(ctx).BuildBatchUpsert(new[]
        {
            new Row { Id = 1, Flags = 1UL, Name = "a" }, new Row { Id = 2, Flags = 2UL, Name = "b" }
        });

        Assert.Equal(2, batch.Count);
        Assert.All(batch, sc => Assert.DoesNotContain("VALUES(\"flags\")", sc.Query.ToString()));
    }

    [Fact]
    public void PrimaryKeyGateway_BatchUpsert_WithAUInt64Column_RunsOneStatementPerRow()
    {
        using var ctx = Context(SupportedDatabase.TiDb);
        var batch = new PrimaryKeyTableGateway<PkRow>(ctx).BuildBatchUpsert(new[]
        {
            new PkRow { K = 1, Flags = 1UL, Name = "a" }, new PkRow { K = 2, Flags = 2UL, Name = "b" }
        });

        Assert.Equal(2, batch.Count);
        Assert.All(batch, sc => Assert.DoesNotContain("VALUES(\"flags\")", sc.Query.ToString()));
    }

    [Fact]
    public void TiDb_BatchUpsert_WithNoColumnThatCouldBeBit64_StaysOneStatement()
    {
        using var ctx = Context(SupportedDatabase.TiDb);
        var batch = new TableGateway<PlainRow, int>(ctx).BuildBatchUpsert(new[] { new PlainRow { Id = 1, N = 1, S = "a" }, new PlainRow { Id = 2, N = 2, S = "b" } });

        Assert.Single(batch);
    }

    [Fact]
    public void MySql_UInt64Upsert_IsUnchanged()
    {
        using var ctx = Context(SupportedDatabase.MySql);
        var gateway = new TableGateway<Row, int>(ctx);
        var batch = gateway.BuildBatchUpsert(new[] { new Row { Id = 1, Flags = 1UL }, new Row { Id = 2, Flags = 2UL } });

        Assert.Single(batch);
    }

    [Fact]
    public void TableGateway_SingleRowUpsert_SetsLongAndBinaryColumnsFromTheirParameters()
    {
        using var ctx = Context(SupportedDatabase.TiDb);
        using var sc = new TableGateway<WideRow, int>(ctx).BuildUpsert(
            new WideRow { Id = 1, N = 5, Raw = new byte[] { 1, 2 }, Name = "a" });
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("VALUES(\"n\")", sql);
        Assert.DoesNotContain("VALUES(\"raw\")", sql);
        Assert.Contains("\"name\" = VALUES(\"name\")", sql);
    }

    [Fact]
    public void TableGateway_BatchUpsert_WithALongColumn_RunsOneStatementPerRow()
    {
        using var ctx = Context(SupportedDatabase.TiDb);
        var batch = new TableGateway<WideRow, int>(ctx).BuildBatchUpsert(new[]
        {
            new WideRow { Id = 1, N = 1, Name = "a" }, new WideRow { Id = 2, N = 2, Name = "b" }
        });

        Assert.Equal(2, batch.Count);
        Assert.All(batch, sc => Assert.DoesNotContain("VALUES(\"n\")", sc.Query.ToString()));
    }
}
