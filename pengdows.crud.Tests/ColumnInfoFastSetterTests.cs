using System.Data;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-016: id/version/correlation write-backs use a compiled setter, with
/// PropertyInfo.SetValue's semantics (null into a non-nullable value type sets its default, a
/// private setter works).
/// </summary>
public class ColumnInfoFastSetterTests
{
    [Table("setter_rows")]
    private sealed class Row
    {
        [Id(false)] [Column("id", DbType.Int64)] public long Id { get; private set; }
        [Column("maybe", DbType.Int32)] public int? Maybe { get; set; }
        [Column("name", DbType.String)] public string? Name { get; set; }
    }

    private static ColumnInfo Column(string name) =>
        (ColumnInfo)new TypeMapRegistry().GetTableInfo<Row>().Columns[name];

    [Fact]
    public void FastSetter_SetsPrivateSetterProperty()
    {
        var row = new Row();

        Column("id").FastSetter!(row, 42L);

        Assert.Equal(42L, row.Id);
    }

    [Fact]
    public void FastSetter_NullIntoNonNullableValueType_SetsDefault_AsReflectionDoes()
    {
        var row = new Row();
        Column("id").FastSetter!(row, 5L);

        Column("id").FastSetter!(row, null);

        Assert.Equal(0L, row.Id);
    }

    [Fact]
    public void FastSetter_NullableAndReference()
    {
        var row = new Row { Maybe = 1, Name = "x" };

        Column("maybe").FastSetter!(row, 7);
        Column("name").FastSetter!(row, null);

        Assert.Equal(7, row.Maybe);
        Assert.Null(row.Name);
        Column("maybe").FastSetter!(row, null);
        Assert.Null(row.Maybe);
    }
}
