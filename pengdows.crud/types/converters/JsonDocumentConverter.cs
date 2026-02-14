// =============================================================================
// FILE: JsonDocumentConverter.cs
// PURPOSE: Converter for System.Text.Json.JsonDocument types.
//
// AI SUMMARY:
// - Converts between JsonDocument and string for database storage.
// - ConvertToProvider(): Serializes JsonDocument to JSON string via JsonSerializer.
// - TryConvertFromProvider(): Parses string back to JsonDocument.
// - Handles JsonDocument pass-through and string input.
// - Registered by default in AdvancedTypeRegistry for JSON column support.
// - Thread-safe and stateless.
// =============================================================================

using System.Text.Json;
using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between <see cref="JsonDocument"/> and string for database storage.
/// Serializes to JSON string on write, parses back to JsonDocument on read.
/// </summary>
internal sealed class JsonDocumentConverter : AdvancedTypeConverter<JsonDocument>
{
    protected override object? ConvertToProvider(JsonDocument value, SupportedDatabase provider)
    {
        return JsonSerializer.Serialize(value);
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out JsonDocument result)
    {
        if (value is JsonDocument doc)
        {
            result = doc;
            return true;
        }

        if (value is string json)
        {
            try
            {
                result = JsonDocument.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                result = default!;
                return false;
            }
        }

        result = default!;
        return false;
    }
}
