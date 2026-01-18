using System;
using System.Text.Json;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.valueobjects;

public static class JsonValueTests
{
    [Fact]
    public static void AsString_RoundTripsRawJsonWithoutSerialization()
    {
        var json = "{\"id\":1,\"name\":\"pengdows\"}";
        var value = new JsonValue(json);

        Assert.Equal(json, value.AsString());
        using var document = value.AsDocument();
        Assert.Equal("pengdows", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public static void AsString_FromDocument_SerializesLazyOnlyOnce()
    {
        using var document = JsonDocument.Parse("{\"flag\":true}");
        var value = new JsonValue(document);

        var first = value.AsString();
        var second = value.AsString();

        Assert.Equal("{\"flag\":true}", first);
        Assert.Equal(first, second); // cached result is consistent
    }

    [Fact]
    public static void AsElement_FromString_ReturnsIndependentClone()
    {
        var value = new JsonValue("{\"count\":5}");

        var element = value.AsElement();
        Assert.Equal(5, element.GetProperty("count").GetInt32());

        // Modify clone and ensure original string remains unchanged
        using var doc = JsonDocument.Parse(element.GetRawText());
        Assert.Equal("{\"count\":5}", value.AsString());
    }

    [Fact]
    public static void Parse_EmptyStringNormalizesToNullLiteral()
    {
        var parsed = JsonValue.Parse("   ");
        Assert.Equal("null", parsed.AsString());
    }

    [Fact]
    public static void Equality_UsesStringRepresentation()
    {
        using var doc = JsonDocument.Parse("{\"x\":1}");
        var fromDoc = new JsonValue(doc);
        var fromString = new JsonValue("{\"x\":1}");

        Assert.True(fromDoc == fromString);
        Assert.Equal(fromDoc.GetHashCode(), fromString.GetHashCode());
        Assert.True(fromDoc.Equals((object)fromString));
    }

    [Fact]
    public static void FromObject_And_ToObject_RoundTrip()
    {
        var payload = new TestPayload { Name = "codex", Count = 7 };

        var json = JsonValue.FromObject(payload);
        var roundTrip = json.ToObject<TestPayload>();

        Assert.Equal(payload.Name, roundTrip.Name);
        Assert.Equal(payload.Count, roundTrip.Count);
    }

    private sealed class TestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
