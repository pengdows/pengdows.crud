using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

[Table("hana_arrays")]
public class HanaArrayElementEntity
{
    [Id]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("shorts", DbType.Object)]
    public short[] Shorts { get; set; } = System.Array.Empty<short>();

    [Column("reals", DbType.Object)]
    public float[] Reals { get; set; } = System.Array.Empty<float>();

    [Column("ints", DbType.Object)]
    public int[] Ints { get; set; } = System.Array.Empty<int>();
}

/// <summary>
/// A SAP HANA array's element type is the subquery's column type: an ARRAY(SELECT V ...) over JSON_TABLE's INT or DOUBLE
/// builds an INTEGER or DOUBLE array. A SMALLINT or REAL element is named by a CAST in the subquery, so the array is that
/// type whatever column it is written to (JSON_TABLE types no SMALLINT or REAL column itself).
/// </summary>
public class HanaArrayElementTests
{
    private static string InsertSql()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SapHana", new fakeDbFactory(SupportedDatabase.SapHana));
        var gateway = new TableGateway<HanaArrayElementEntity, int>(context);
        using var sc = gateway.BuildCreate(new HanaArrayElementEntity
        {
            Id = 1, Shorts = new short[] { 1 }, Reals = new float[] { 1.5f }, Ints = new[] { 2 },
        });
        return sc.Query.ToString();
    }

    [Fact]
    public void ASmallintElementIsNamedByACastInTheSubquery()
    {
        Assert.Contains("ARRAY(SELECT CAST(V AS SMALLINT) FROM JSON_TABLE(", InsertSql());
    }

    [Fact]
    public void ARealElementIsNamedByACastInTheSubquery()
    {
        Assert.Contains("ARRAY(SELECT CAST(V AS REAL) FROM JSON_TABLE(", InsertSql());
    }

    [Fact]
    public void AnIntegerElementNeedsNoCast()
    {
        Assert.Contains("ARRAY(SELECT V FROM JSON_TABLE(", InsertSql());
    }
}
