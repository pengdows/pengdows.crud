// =============================================================================
// FILE: JsonCoercion.cs
// PURPOSE: JSON text: reading any JSON-carrying input as text or as a document, writing values as JSON, and the JSON null.
//
// AI SUMMARY:
// - ExtractJsonString / GetJsonText: the text of text, bytes, a character buffer, a stream, a document, an element, a node,
//   or an object to serialise. Blank text is the JSON null (JsonTextOrNullLiteral).
// - ToJsonDocument: the document of any of those inputs.
// - CoerceJsonValue: a read into the JSON value types (JsonValue, JsonDocument, JsonNode, typed POCOs).
// - CarriesJsonText: whether an input is JSON text, so a caller can read it as text.
// =============================================================================

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.enums;

namespace pengdows.crud.types.coercion;

internal static class JsonCoercion
{
    internal static object? CoerceJsonValue(
        object value,
        Type targetType,
        TypeCoercionOptions options)
    {
        var actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualTarget.IsInstanceOfType(value))
        {
            return value;
        }

        var serializerOptions = JsonSerializerOptions.Default;

        if (actualTarget == typeof(string) || actualTarget == typeof(ReadOnlyMemory<char>))
        {
            var text = ExtractJsonString(value, serializerOptions);
            return actualTarget == typeof(string) ? text : new ReadOnlyMemory<char>(text.ToCharArray());
        }

        if (actualTarget == typeof(JsonDocument))
        {
            return ToJsonDocument(value, serializerOptions);
        }

        if (actualTarget == typeof(JsonElement))
        {
            using var doc = ToJsonDocument(value, serializerOptions);
            return doc.RootElement.Clone();
        }

        if (actualTarget == typeof(types.valueobjects.JsonValue))
        {
            // The JSON text itself, never JsonSerializer.Deserialize into the struct (TYPE-019).
            return new types.valueobjects.JsonValue(JsonTextOrNullLiteral(ExtractJsonString(value, serializerOptions)));
        }

        if (actualTarget == typeof(JsonNode))
        {
            return JsonNode.Parse(JsonTextOrNullLiteral(ExtractJsonString(value, serializerOptions)), new JsonNodeOptions());
        }

        var jsonText = ExtractJsonString(value, serializerOptions);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            if (value is Stream)
            {
                throw new JsonException("JSON payload cannot be empty.");
            }

            jsonText = JsonTextOrNullLiteral(jsonText);
        }

        try
        {
            // Into the declared type, so JSON null reads as null for a Nullable<T> (COR-007).
            return JsonSerializer.Deserialize(jsonText, targetType, serializerOptions);
        }
        catch (JsonException ex) when (value is not Stream)
        {
            TypeCoercionHelper.TryLogDebug("Failed to deserialize JSON payload into {TargetType}", actualTarget);
            throw new JsonException($"Failed to deserialize JSON payload into {actualTarget}.", ex);
        }
    }

    /// <summary>
    /// COR-007: blank text (empty or whitespace) in a JSON column is the JSON literal <c>null</c> on
    /// every read path, so each target reads it as it reads <c>null</c>: null for a reference or
    /// nullable type, a JSON-null document or value, and a failure for a non-nullable value type.
    /// </summary>
    internal static bool IsJsonValueType(Type type) =>
        type == typeof(JsonDocument) || type == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(type) ||
        type == typeof(types.valueobjects.JsonValue);

    /// <summary>
    /// A scalar read into a string: its canonical invariant text, exact to the tick (ISO 8601 round-trip
    /// for timestamps, yyyy-MM-dd, HH:mm:ss.FFFFFFF, the TimeSpan "c" form, a Guid's "D" form). A
    /// DateTime read as "MM/dd/yyyy HH:mm:ss" without its fraction and the others failed (DRY-010).
    /// </summary>
    internal static string JsonTextOrNullLiteral(string? text) => string.IsNullOrWhiteSpace(text) ? "null" : text;

    // DRY-015: every JSON-carrying input goes through ExtractJsonString; blank is the JSON null (COR-007).
    internal static JsonDocument ToJsonDocument(object value, JsonSerializerOptions options)
    {
        if (value is JsonDocument doc)
        {
            return doc;
        }

        try
        {
            return JsonDocument.Parse(JsonTextOrNullLiteral(ExtractJsonString(value, options)));
        }
        catch (JsonException ex) when (value is not Stream)
        {
            TypeCoercionHelper.TryLogDebug("Failed to deserialize JSON payload into {TargetType}", typeof(JsonDocument));
            throw new JsonException($"Failed to deserialize JSON payload into {typeof(JsonDocument)}.", ex);
        }
    }

    /// <summary>
    /// The JSON text an input carries (string, UTF-8 bytes, segment, memory, stream, char[], JSON DOM),
    /// the one reader every JSON target uses (DRY-015). Empty binary input is empty text.
    /// </summary>
    internal static string ExtractJsonString(object value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case string jsonText:
                return jsonText;
            case JsonElement element:
                return element.GetRawText();
            case JsonDocument document:
                return document.RootElement.GetRawText();
            case JsonNode node:
                return node.ToJsonString(options);
            case byte[] bytes:
                return bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
            case ArraySegment<byte> segment:
                return segment.Count == 0 ? string.Empty : Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count);
            case ReadOnlyMemory<byte> memory:
                return Encoding.UTF8.GetString(memory.Span);
            case Stream stream:
                return StreamToString(stream);
            case char[] chars:
                return new string(chars);
            default:
                return JsonSerializer.Serialize(value, options);
        }
    }

    /// <summary>True for the inputs that carry JSON text (what <see cref="ExtractJsonString"/> reads as text).</summary>
    internal static bool CarriesJsonText(object value) =>
        value is string or byte[] or ArraySegment<byte> or ReadOnlyMemory<byte> or Stream or char[] or JsonNode;

    internal static string GetJsonText(object value, JsonSerializerOptions? options = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return ExtractJsonString(value, options ?? JsonSerializerOptions.Default);
    }

    private static string StreamToString(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Resolves a coercion delegate for a known source/target type pair once per plan (DataReaderMapper),
    /// converting with the reader's dialect options: its provider-specific coercions and Guid byte order (TYPE-002).
    /// </summary>
}
