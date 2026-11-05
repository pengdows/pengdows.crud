using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

[Table("native_xml")]
public class NativeXmlEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("doc", DbType.String, NativeType = "xml")]
    public string Doc { get; set; } = string.Empty;
}

[Table("native_xid8")]
public class NativeXid8Entity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    // A ulong binds as numeric on PostgreSQL, which has no implicit cast to xid8: the value goes through text.
    [Column("txid", DbType.UInt64, NativeType = "xid8")]
    public ulong Txid { get; set; }
}

[Table("native_typed")]
public class NativeTypedEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    // A decimal property written to a DECFLOAT column: the declared native type says which column this is.
    [Column("v", DbType.Decimal, NativeType = "DECFLOAT")]
    public decimal V { get; set; }
}

/// <summary>
/// [Column(NativeType)] declares the column's native type, which a CLR type alone cannot say (a decimal is NUMERIC or
/// DECFLOAT). A dialect that sends such a value as text casts it to the declared type in the statement.
/// </summary>
public class NativeColumnTypeTests
{
    [Fact]
    public void NativeTypeIsReadFromTheColumnAttribute()
    {
        var attribute = (ColumnAttribute)Attribute.GetCustomAttribute(
            typeof(NativeTypedEntity).GetProperty(nameof(NativeTypedEntity.V))!, typeof(ColumnAttribute))!;

        Assert.Equal("DECFLOAT", attribute.NativeType);
    }

    [Fact]
    public void FirebirdCastsTheDeclaredNativeTypeInTheInsert()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", new fakeDbFactory(SupportedDatabase.Firebird));
        var gateway = new TableGateway<NativeTypedEntity, int>(context);

        using var sc = gateway.BuildCreate(new NativeTypedEntity { Id = 1, V = 1.5m });

        Assert.Contains("CAST(CAST(", sc.Query.ToString());
        Assert.Contains("AS DECFLOAT)", sc.Query.ToString());
    }

    [Fact]
    public void AColumnWithoutANativeTypeIsUnchanged()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", new fakeDbFactory(SupportedDatabase.Firebird));
        var gateway = new TableGateway<WideIntegerSelectEntity, int>(context);

        using var sc = gateway.BuildCreate(new WideIntegerSelectEntity { Id = 1, N = 7 });

        Assert.DoesNotContain("DECFLOAT", sc.Query.ToString());
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    [InlineData(SupportedDatabase.CockroachDb)]
    public void PostgreSqlFamilyCastsTheTextToTheDeclaredNativeType(SupportedDatabase product)
    {
        // A text parameter into an xml, tsvector, pg_lsn, point or xid column: PostgreSQL refuses the text type, and an
        // explicit cast to the declared type is the conversion the server accepts.
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<NativeXmlEntity, int>(context);

        using var sc = gateway.BuildCreate(new NativeXmlEntity { Id = 1, Doc = "<a>1</a>" });

        Assert.Contains("CAST(", sc.Query.ToString());
        Assert.Contains(" AS xml)", sc.Query.ToString());
    }

    [Fact]
    public void PostgreSqlCastsTheValueThroughTextToTheDeclaredNativeType()
    {
        // numeric has no implicit cast to a native type such as xid8: the text of the value is the bridge.
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql));
        var gateway = new TableGateway<NativeXid8Entity, int>(context);

        using var sc = gateway.BuildCreate(new NativeXid8Entity { Id = 1, Txid = 4294967296ul });

        Assert.Contains("CAST(CAST(", sc.Query.ToString());
        Assert.Contains(" AS text) AS xid8)", sc.Query.ToString());
    }
}
