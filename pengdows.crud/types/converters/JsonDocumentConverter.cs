// =============================================================================
// FILE: JsonDocumentConverter.cs
// PURPOSE: Converter for System.Text.Json.JsonDocument types.
//
// AI SUMMARY:
// - Converts between JsonDocument and string for database storage.
// - ConvertToProvider(): Serializes JsonDocument to JSON string via JsonSerializer.
// - TryConvertFromProvider(): JsonDocument pass-through, JsonElement, and any JSON-carrying input
//   (text, UTF-8 bytes, streams...) through TypeCoercionHelper.ExtractJsonString.
// - Registered by default in AdvancedTypeRegistry for JSON column support.
// - Thread-safe and stateless.
// =============================================================================

using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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

        // Every JSON-carrying input through the one reader (DRY-015); blank is the JSON null (COR-007).
        if (value is JsonElement element)
        {
            result = JsonDocument.Parse(element.GetRawText());
            return true;
        }

        if (TypeCoercionHelper.CarriesJsonText(value))
        {
            try
            {
                result = JsonDocument.Parse(TypeCoercionHelper.JsonTextOrNullLiteral(
                    TypeCoercionHelper.ExtractJsonString(value, JsonSerializerOptions.Default)));
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