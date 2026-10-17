using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Audit G-06, proven on live databases (NullableKeyUpsertTests): a NULL in an upsert's conflict key never matches, in
/// MERGE (ON t.k = s.k), ON CONFLICT or ON DUPLICATE KEY alike. Upserting (7, NULL) twice left 2 rows on SQLite,
/// PostgreSQL and MySQL (silent duplicates) and raised a unique violation on SQL Server. An upsert on a key that cannot
/// match is meaningless, so it is refused up front, naming the column.
/// </summary>
public class NullUpsertKeyTests
{
    [Table("null_key_pairs")]
    public class PairRow
    {
        [PrimaryKey(1)] [Column("a", DbType.Int64)] public long A { get; set; }
        [PrimaryKey(2)] [Column("b", DbType.Int64)] public long? B { get; set; }
        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    [Table("null_key_codes")]
    public class CodeRow
    {
        [Id(false)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [PrimaryKey(1)] [Column("code", DbType.String)] public string? Code { get; set; }
        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    private static DatabaseContext Context(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

    public static IEnumerable<object[]> Databases() => new[]
    {
        new object[] { SupportedDatabase.PostgreSql }, new object[] { SupportedDatabase.SqlServer },
        new object[] { SupportedDatabase.MySql }, new object[] { SupportedDatabase.Sqlite },
        new object[] { SupportedDatabase.Oracle }
    };

    [Theory]
    [MemberData(nameof(Databases))]
    public void PrimaryKeyGateway_BuildUpsert_RefusesANullKeyComponent(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new PrimaryKeyTableGateway<PairRow>(context);

        var ex = Assert.Throws<InvalidOperationException>(() => gateway.BuildUpsert(new PairRow { A = 7, B = null, Value = 1 }));

        Assert.Contains("'b'", ex.Message);
        Assert.Contains(nameof(PairRow), ex.Message);
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void PrimaryKeyGateway_BuildBatchUpsert_RefusesANullKeyComponent(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new PrimaryKeyTableGateway<PairRow>(context);
        var rows = new List<PairRow>
        {
            new() { A = 7, B = 1, Value = 1 },
            new() { A = 7, B = null, Value = 2 }
        };

        Assert.Throws<InvalidOperationException>(() => gateway.BuildBatchUpsert(rows));
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void TableGateway_BuildUpsert_RefusesANullKeyComponent(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new TableGateway<CodeRow, long>(context);

        var ex = Assert.Throws<InvalidOperationException>(() => gateway.BuildUpsert(new CodeRow { Id = 1, Code = null, Value = 1 }));

        Assert.Contains("'code'", ex.Message);
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void TableGateway_BuildBatchUpsert_RefusesANullKeyComponent(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new TableGateway<CodeRow, long>(context);
        var rows = new List<CodeRow>
        {
            new() { Id = 1, Code = "a", Value = 1 },
            new() { Id = 2, Code = null, Value = 2 }
        };

        Assert.Throws<InvalidOperationException>(() => gateway.BuildBatchUpsert(rows));
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void AKeyWithEveryComponentSet_IsStillBuilt(SupportedDatabase db)
    {
        using var context = Context(db);
        var pairs = new PrimaryKeyTableGateway<PairRow>(context);
        var codes = new TableGateway<CodeRow, long>(context);

        using var single = pairs.BuildUpsert(new PairRow { A = 7, B = 1, Value = 1 });
        var batch = codes.BuildBatchUpsert(new List<CodeRow> { new() { Id = 1, Code = "a" }, new() { Id = 2, Code = "b" } });

        Assert.False(string.IsNullOrEmpty(single.Query.ToString()));
        Assert.NotEmpty(batch);
    }
}
