using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.types.converters;
using pengdows.crud.wrappers;
using JsonValue = pengdows.crud.types.valueobjects.JsonValue;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// COR-007: one rule for blank text (empty or whitespace) in a JSON column, on every read path: it
/// reads as the JSON literal <c>null</c>. A reference or nullable target gets null, JsonDocument a
/// JSON-null document, JsonValue the "null" value, and a non-nullable value type fails as JSON null
/// does (DataMappingException through the gateway; 2.0.5 read default(T) there, a silent wrong value,
/// TYPE-008). Invalid non-blank JSON keeps failing (DEC-008).
/// </summary>
public class JsonBlankTextTests : SqlLiteContextTestBase
{
    public sealed class Payload
    {
        public string? Name { get; set; }
    }

    [Table("json_rows")]
    public sealed class Row
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("Data", DbType.String)] public Payload? Data { get; set; }
        [Json] [Column("Count", DbType.String)] public int Count { get; set; }
        [Json] [Column("MaybeCount", DbType.String)] public int? MaybeCount { get; set; }
        [Json] [Column("Raw", DbType.String)] public JsonValue? Raw { get; set; }
    }

    public static IEnumerable<object[]> BlankTexts() => new[] { new object[] { "" }, new object[] { "   " } };

    [Theory]
    [MemberData(nameof(BlankTexts))]
    public void Gateway_BlankJson_ReadsAsJsonNull(string blank)
    {
        var entity = Map(new Dictionary<string, object>
        {
            ["Id"] = 1, ["Data"] = blank, ["Count"] = "3", ["MaybeCount"] = blank, ["Raw"] = blank
        });

        Assert.Null(entity.Data);
        Assert.Null(entity.MaybeCount);
        Assert.Equal("null", entity.Raw!.Value.ToString());
    }

    [Theory]
    [MemberData(nameof(BlankTexts))]
    public void Gateway_BlankJsonIntoNonNullableValueType_FailsNamingTheColumn(string blank)
    {
        var ex = Assert.Throws<DataMappingException>(() => Map(new Dictionary<string, object>
        {
            ["Id"] = 1, ["Data"] = "{}", ["Count"] = blank, ["MaybeCount"] = "1", ["Raw"] = "{}"
        }));

        Assert.Contains("Count", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BlankTexts))]
    public void Coerce_BlankJson_ReadsAsJsonNull(string blank)
    {
        Assert.Null(TypeCoercionHelper.Coerce(blank, typeof(string), typeof(JsonNode)));
        Assert.Equal("null", ((JsonValue)TypeCoercionHelper.Coerce(blank, typeof(string), typeof(JsonValue))!).ToString());
        Assert.Equal(JsonValueKind.Null, ((JsonDocument)TypeCoercionHelper.Coerce(blank, typeof(string), typeof(JsonDocument))!).RootElement.ValueKind);
    }

    [Theory]
    [MemberData(nameof(BlankTexts))]
    public void JsonDocumentConverter_BlankJson_ReadsAsJsonNull(string blank)
    {
        Assert.True(new JsonDocumentConverter().TryConvertFromProvider(blank, SupportedDatabase.PostgreSql, out var document));

        Assert.Equal(JsonValueKind.Null, document.RootElement.ValueKind);
    }

    [Fact]
    public void Gateway_InvalidNonBlankJson_StillFails()
    {
        Assert.Throws<DataMappingException>(() => Map(new Dictionary<string, object>
        {
            ["Id"] = 1, ["Data"] = "{bad}", ["Count"] = "1", ["MaybeCount"] = "1", ["Raw"] = "{}"
        }));
    }

    private Row Map(Dictionary<string, object> row)
    {
        var gateway = new TableGateway<Row, int>(Context);
        using var reader = new TableGatewayConverterTests.FakeTrackedReader(new[] { row });
        reader.Read();
        return gateway.MapReaderToObject(reader);
    }
}
