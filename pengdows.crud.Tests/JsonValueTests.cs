using System;
using System.Text.Json;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class JsonValueTests
{
    [Fact]
    public void AsString_ReturnsRawJsonWhenConstructedFromString()
    {
        var json = new JsonValue("{\"name\":\"pengdows\"}");

        Assert.Equal("{\"name\":\"pengdows\"}", json.AsString());
    }

    [Fact]
    public void AsString_SerializesDocumentAndElement()
    {
        using var doc = JsonDocument.Parse("{\"id\":1}");
        var fromDocument = new JsonValue(doc);
        var fromElement = new JsonValue(doc.RootElement);

        Assert.Equal("{\"id\":1}", fromDocument.AsString());
        Assert.Equal("{\"id\":1}", fromElement.AsString());
    }

    [Fact]
    public void AsDocument_ReusesExistingOrParsesRawText()
    {
        using var doc = JsonDocument.Parse("{\"value\":42}");
        var fromDocument = new JsonValue(doc);
        var parsed = fromDocument.AsDocument();
        Assert.Same(doc, parsed);

        var fromRaw = new JsonValue("{\"value\":42}");
        using var parsedRaw = fromRaw.AsDocument();
        Assert.Equal(42, parsedRaw.RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Parse_BlankTextReturnsNullLiteral()
    {
        var parsed = JsonValue.Parse("   ");

        Assert.Equal("null", parsed.AsString());
    }

    [Fact]
    public void FromObject_UsesSerializerOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        var json = JsonValue.FromObject(new { Flag = true }, options);

        Assert.Equal("{\"Flag\":true}", json.AsString());
    }

    [Fact]
    public void ToObject_ThrowsWhenDeserializerReturnsNull()
    {
        var json = new JsonValue("null");

        Assert.Throws<InvalidOperationException>(() => json.ToObject<object>());
    }

    [Fact]
    public void Equality_UsesStringComparison()
    {
        var original = new JsonValue("{\"n\":1}");
        using var doc = JsonDocument.Parse("{\"n\":1}");
        var clone = new JsonValue(doc);

        Assert.True(original == clone);
        Assert.False(original != clone);
        Assert.True(original.Equals((object)clone));
        Assert.Equal(original.GetHashCode(), clone.GetHashCode());
    }
}