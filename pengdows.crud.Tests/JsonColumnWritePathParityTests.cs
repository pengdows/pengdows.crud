using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-047: a [Json] column must be written the same way by every write path. The single-row
/// binder wrote a [Json] string raw while batch and the primary-key gateway (ColumnInfo) wrote it
/// JSON-encoded, and the read side deserializes a JSON string, so the single-row form read back as
/// null. A null [Json] reference was the text "null" single-row but SQL NULL in batch.
/// </summary>
public sealed class JsonColumnWritePathParityTests
{
    [Table("json_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("doc", DbType.String)] public string? Doc { get; set; }
        [Json] [Column("meta", DbType.String)] public Dictionary<string, int>? Meta { get; set; }
    }

    private static TableGateway<Row, int> Gateway(SupportedDatabase product) =>
        new(new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product)));

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void BuildCreate_JsonString_IsWrittenJsonEncoded_LikeTheOtherPaths(SupportedDatabase product)
    {
        var row = new Row { Id = 1, Doc = "{\"a\":1}" };
        var column = new TypeMapRegistry().GetTableInfo<Row>().Columns["Doc"];

        using var sc = Gateway(product).BuildCreate(row);

        Assert.Equal(JsonSerializer.Serialize("{\"a\":1}"), sc.GetParameterValue("i1"));
        Assert.Equal(column.MakeParameterValueFromField(row), sc.GetParameterValue("i1"));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void BuildCreate_NullJsonReference_IsSqlNull(SupportedDatabase product)
    {
        using var sc = Gateway(product).BuildCreate(new Row { Id = 1, Meta = null });

        var value = sc.GetParameterValue("i2");
        Assert.True(value is null or System.DBNull, $"bound {value ?? "<null>"}");
    }
}
