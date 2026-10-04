using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-020 / WRT-007, confirmed live (PostgreSQL 16.4, CockroachDB 25.1, YugabyteDB 2025.2, Npgsql
/// 9.0.3): a C# enum stored by name into a user-defined ENUM column fails in a MERGE upsert's and a
/// batch update's VALUES source ("column is of type mood but expression is of type text"; CockroachDB:
/// "value type string doesn't match type mood"): a VALUES list types its values as text by itself.
/// A typed source does not: an empty SELECT of the target's own columns, then one SELECT per row, all
/// UNION ALL, so each value takes the column's type with no declared type name needed. Used only
/// when a row has such an enum; every other batch keeps VALUES.
/// </summary>
public sealed class PostgreSqlEnumSourceTests
{
    public enum Mood { sad, ok, happy }

    [Table("type_rt")]
    public sealed class EnumRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public Mood V { get; set; }
    }

    [Table("type_rt")]
    public sealed class PlainRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public string V { get; set; } = "";
    }

    private static DatabaseContext Context(SupportedDatabase database, string version) =>
        new($"Data Source=x;EmulatedProduct={database}", new fakeDbFactory(database) { ServerVersion = version });

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, "16.4")]
    [InlineData(SupportedDatabase.CockroachDb, "25.1")]
    [InlineData(SupportedDatabase.YugabyteDb, "2.20")]
    public async Task BatchUpdate_EnumByName_UsesATypedSource(SupportedDatabase database, string version)
    {
        await using var context = Context(database, version);
        var gateway = new TableGateway<EnumRow, int>(context);

        var sql = Assert.Single(gateway.BuildBatchUpdate(new[] { new EnumRow { Id = 1 }, new EnumRow { Id = 2 } })).Query.ToString();

        Assert.Contains("FROM (SELECT \"id\", \"v\" FROM \"type_rt\" WHERE FALSE UNION ALL SELECT @b0, @b1 UNION ALL SELECT @b2, @b3) AS s(\"id\", \"v\")", sql);
        Assert.DoesNotContain("VALUES", sql);
    }

    [Fact]
    public async Task BatchUpdate_WithoutAnEnumByName_KeepsValues()
    {
        await using var context = Context(SupportedDatabase.PostgreSql, "16.4");
        var gateway = new TableGateway<PlainRow, int>(context);

        var sql = Assert.Single(gateway.BuildBatchUpdate(new[] { new PlainRow { Id = 1 }, new PlainRow { Id = 2 } })).Query.ToString();

        Assert.Contains("FROM (VALUES (@b0, @b1), (@b2, @b3)) AS s(", sql);
    }

    [Fact]
    public async Task MergeUpsert_EnumByName_UsesATypedSource()
    {
        await using var context = Context(SupportedDatabase.PostgreSql, "16.4");
        var gateway = new TableGateway<EnumRow, int>(context);

        var sql = gateway.BuildUpsert(new EnumRow { Id = 1, V = Mood.ok }).Query.ToString();

        Assert.StartsWith("MERGE INTO", sql);
        Assert.Contains("USING (SELECT \"id\", \"v\" FROM \"type_rt\" WHERE FALSE UNION ALL SELECT @i0, @i1) AS s (\"id\", \"v\")", sql);
    }

    [Fact]
    public async Task MergeUpsert_WithoutAnEnumByName_KeepsValues()
    {
        await using var context = Context(SupportedDatabase.PostgreSql, "16.4");
        var gateway = new TableGateway<PlainRow, int>(context);

        var sql = gateway.BuildUpsert(new PlainRow { Id = 1, V = "x" }).Query.ToString();

        Assert.Contains("USING (VALUES (@i0, @i1)) AS s (\"id\", \"v\")", sql);
    }
}
