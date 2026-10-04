using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// HARN-011 (found live on InterBase): a database with no upsert statement refused a versioned
// PrimaryKeyTableGateway upsert claiming the entity "has only primary key columns", because the
// pure-key check ran before the capability check. The refusal must name the real cause.
public class PrimaryKeyGatewayUpsertUnsupportedTests
{
    [Table("pk_upsert")]
    public class Row
    {
        [PrimaryKey(1)] [Column("k", DbType.Int32)] public int K { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version] [Column("version", DbType.Int32)] public int Version { get; set; }
    }

    [Fact]
    public void BuildUpsert_DatabaseWithNoUpsertStatement_SaysUpsertIsNotSupported()
    {
        var factory = new fakeDbFactory(SupportedDatabase.InterBase);
        using var context = new DatabaseContext("Data Source=x;EmulatedProduct=InterBase", factory);
        var gateway = new PrimaryKeyTableGateway<Row>(context);

        var ex = Assert.Throws<NotSupportedException>(() => gateway.BuildUpsert(new Row { K = 1, Name = "a" }));

        Assert.Equal("Upsert not supported for InterBase", ex.Message);
    }

    [Fact]
    public void BuildBatchUpsert_DatabaseWithNoUpsertStatement_SaysUpsertIsNotSupported()
    {
        var factory = new fakeDbFactory(SupportedDatabase.InterBase);
        using var context = new DatabaseContext("Data Source=x;EmulatedProduct=InterBase", factory);
        var gateway = new PrimaryKeyTableGateway<Row>(context);

        var ex = Assert.Throws<NotSupportedException>(() =>
            gateway.BuildBatchUpsert(new[] { new Row { K = 1, Name = "a" }, new Row { K = 2, Name = "b" } }));

        Assert.Equal("Upsert not supported for InterBase", ex.Message);
    }
}
