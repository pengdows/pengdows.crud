using System.Collections.Generic;
using System.Data;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-019: a JsonValue property hydrates from the JSON text the provider returns (Npgsql
/// returns jsonb as a string) and holds exactly that JSON.
/// </summary>
public class JsonValueColumnReadTests
{
    [Table("json_value_rows")]
    public sealed class Row
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("doc", DbType.Object)] public pengdows.crud.types.valueobjects.JsonValue Doc { get; set; }
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    public async Task RetrieveOneAsync_JsonValueColumn_HoldsTheStoredJson(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        var execConn = new fakeDbConnection { EmulatedProduct = product };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["doc"] = "{\"kind\": \"value\", \"count\": 3}" }
        });
        factory.Connections.Add(execConn);
        var typeMap = new TypeMapRegistry();
        await using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory, typeMap);
        var gateway = new TableGateway<Row, int>(ctx);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.NotNull(row);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"kind\":\"value\",\"count\":3}"),
            JsonNode.Parse(row!.Doc.AsString())), row.Doc.AsString());
    }
}
