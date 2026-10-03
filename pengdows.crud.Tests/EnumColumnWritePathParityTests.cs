using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-054: the single-row binder wrote string-stored enums with ToString(), ignoring
/// [EnumLiteral] (batch and primary-key writes honor it through ColumnInfo), and threw for a null
/// nullable enum. Every write path must produce what ColumnInfo produces.
/// </summary>
public sealed class EnumColumnWritePathParityTests
{
    public enum Status
    {
        [EnumLiteral("act")] Active,
        [EnumLiteral("off")] Inactive
    }

    [Table("enum_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("status", DbType.String)] public Status State { get; set; }
        [Column("maybe", DbType.String)] public Status? Maybe { get; set; }
        [Column("code", DbType.Int32)] public Status? Code { get; set; }
    }

    private static TableGateway<Row, int> Gateway() =>
        new(new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite)));

    private static object? Normalize(object? value) => value is System.DBNull ? null : value;

    [Theory]
    [InlineData(Status.Active, null, null)]
    [InlineData(Status.Inactive, Status.Active, Status.Inactive)]
    public void BuildCreate_EnumColumns_BindWhatColumnInfoWrites(Status state, Status? maybe, Status? code)
    {
        var row = new Row { Id = 1, State = state, Maybe = maybe, Code = code };
        var columns = new TypeMapRegistry().GetTableInfo<Row>().Columns;

        using var sc = Gateway().BuildCreate(row);

        Assert.Equal(columns["status"].MakeParameterValueFromField(row), Normalize(sc.GetParameterValue("i1")));
        Assert.Equal(columns["maybe"].MakeParameterValueFromField(row), Normalize(sc.GetParameterValue("i2")));
        Assert.Equal(columns["code"].MakeParameterValueFromField(row), Normalize(sc.GetParameterValue("i3")));
    }

    [Fact]
    public void BuildCreate_LiteralEnum_WritesTheLiteral()
    {
        using var sc = Gateway().BuildCreate(new Row { Id = 1, State = Status.Active });

        Assert.Equal("act", sc.GetParameterValue("i1"));
    }
}
