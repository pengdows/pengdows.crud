using System.Data;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// default(JsonElement) (ValueKind Undefined) cannot be serialized. The gateways read a
/// default-constructed entity while building their SQL templates, so any entity with a
/// JsonElement column failed its first insert with TemplateInitializationException.
/// </summary>
public class JsonElementColumnCreateTests
{
    [Table("json_rows")]
    public sealed class JsonRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("doc", DbType.Object)] public JsonElement Doc { get; set; }
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void BuildCreate_EntityWithJsonElementColumn_BuildsTheInsert(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<JsonRow, int>(context);
        using var document = JsonDocument.Parse("{\"a\":1}");

        using var sc = gateway.BuildCreate(new JsonRow { Id = 1, Doc = document.RootElement.Clone() });

        Assert.Contains("INSERT", sc.Query.ToString());
    }

    [Fact]
    public void DefaultJsonElement_IsWrittenAsNull()
    {
        var column = new TypeMapRegistry().GetTableInfo<JsonRow>().Columns["Doc"];

        Assert.Null(column.MakeParameterValueFromField(new JsonRow { Id = 1 }));
    }
}
