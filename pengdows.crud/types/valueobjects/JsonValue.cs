using System.Text.Json;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Represents a JSON value for database storage with efficient serialization.
/// Optimized for PostgreSQL jsonb, MySQL JSON, SQL Server JSON support.
/// </summary>
public readonly struct JsonValue : IEquatable<JsonValue>
{
    private readonly string? _rawJson;
    private readonly JsonDocument? _document;
    private readonly JsonElement? _element;

    public JsonValue(string jsonText)
    {
        _rawJson = jsonText;
        _document = null;
        _element = null;
    }

    public JsonValue(JsonDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _rawJson = null;
        _element = null;
    }

    public JsonValue(JsonElement element)
    {
        _element = element;
        _rawJson = null;
        _document = null;
    }

    /// <summary>
    /// Get the JSON as a string. Lazy serialization if from JsonDocument/JsonElement.
    /// </summary>
    public string AsString()
    {
        if (_rawJson != null)
        {
            return _rawJson;
        }

        if (_document != null)
        {
            return JsonSerializer.Serialize(_document.RootElement);
        }

        if (_element.HasValue)
        {
            return JsonSerializer.Serialize(_element.Value);
        }

        return "null";
    }

    /// <summary>
    /// Get the JSON as a JsonDocument. Lazy parsing if from string.
    /// </summary>
    public JsonDocument AsDocument()
    {
        if (_document != null)
        {
            return _document;
        }

        var json = _rawJson ?? _element?.GetRawText() ?? "null";
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Get the JSON as a JsonElement.
    /// </summary>
    public JsonElement AsElement()
    {
        if (_element.HasValue)
        {
            return _element.Value;
        }

        using var doc = AsDocument();
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Parse JSON text into a JsonValue.
    /// </summary>
    public static JsonValue Parse(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return new JsonValue("null");
        }

        // Validate by parsing
        using var doc = JsonDocument.Parse(jsonText);
        return new JsonValue(jsonText);
    }

    /// <summary>
    /// Create JsonValue from an object by serializing it.
    /// </summary>
    public static JsonValue FromObject<T>(T value, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(value, options);
        return new JsonValue(json);
    }

    /// <summary>
    /// Deserialize the JSON value to an object.
    /// </summary>
    public T ToObject<T>(JsonSerializerOptions? options = null)
    {
        var element = AsElement();
        return element.Deserialize<T>(options) ?? throw new InvalidOperationException("Deserialization returned null");
    }

    public static implicit operator JsonValue(string jsonText)
    {
        return new JsonValue(jsonText);
    }

    public static implicit operator JsonValue(JsonDocument document)
    {
        return new JsonValue(document);
    }

    public static implicit operator JsonValue(JsonElement element)
    {
        return new JsonValue(element);
    }

    public static implicit operator string(JsonValue jsonValue)
    {
        return jsonValue.AsString();
    }

    public override string ToString()
    {
        return AsString();
    }

    public bool Equals(JsonValue other)
    {
        // Compare the string representations for equality
        return string.Equals(AsString(), other.AsString(), StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsonValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return AsString().GetHashCode(StringComparison.Ordinal);
    }

    public static bool operator ==(JsonValue left, JsonValue right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(JsonValue left, JsonValue right)
    {
        return !left.Equals(right);
    }
}