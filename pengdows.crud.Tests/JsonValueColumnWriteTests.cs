using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A JsonValue column writes the JSON it holds. System.Text.Json serializes the struct's public
/// properties instead (it has none), so every JsonValue column used to be written as "{}".
/// </summary>
public class JsonValueColumnWriteTests
{
    [Table("json_value_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("doc", DbType.Object)] public JsonValue Doc { get; set; }
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Oracle)]
    public void BuildCreate_JsonValueColumn_BindsItsJsonText(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<Row, int>(context);

        using var sc = gateway.BuildCreate(new Row { Id = 1, Doc = JsonValue.Parse("{\"a\":1}") });

        Assert.Equal("{\"a\":1}", sc.GetParameterValue("i1"));
    }

    [Fact]
    public void DefaultJsonValue_IsWrittenAsNull()
    {
        var column = new TypeMapRegistry().GetTableInfo<Row>().Columns["Doc"];

        Assert.Null(column.MakeParameterValueFromField(new Row { Id = 1 }));
    }
}
