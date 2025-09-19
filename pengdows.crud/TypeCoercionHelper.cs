#region

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud;

public static class TypeCoercionHelper
{
    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }
    public static object? Coerce(
        object? value,
        Type dbFieldType,
        IColumnInfo columnInfo,
        EnumParseFailureMode parseMode = EnumParseFailureMode.Throw)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return null;
        }

        var targetType = columnInfo.PropertyInfo.PropertyType;

        if (dbFieldType == targetType)
        {
            return value;
        }

        // Enum coercion
        if (columnInfo.EnumType != null)
        {
            if (Enum.TryParse(columnInfo.EnumType, value?.ToString() ?? string.Empty, true, out var result))
            {
                return result;
            }

            switch (parseMode)
            {
                case EnumParseFailureMode.Throw:
                    throw new ArgumentException($"Cannot convert value '{value}' to enum {columnInfo.EnumType}.");

                case EnumParseFailureMode.SetNullAndLog:
                    Logger.LogWarning(
                        "Cannot convert '{Value}' to non-nullable enum {EnumType}.",
                        value,
                        columnInfo.EnumType);
                    return null;
                // if (Nullable.GetUnderlyingType(targetType) == columnInfo.EnumType)
                //     return null;
                // throw new ArgumentException(
                //     $"Cannot convert '{value}' to non-nullable enum {columnInfo.EnumType}.");

                case EnumParseFailureMode.SetDefaultValue:
                    return Enum.GetValues(columnInfo.EnumType).GetValue(0);
            }
        }

        // JSON deserialization
        if (columnInfo.IsJsonType)
        {
            return CoerceJsonValue(value, targetType, columnInfo);
        }

        return CoerceCore(value!, dbFieldType, targetType);
    }

    public static object? Coerce(
        object? value,
        Type sourceType,
        Type targetType)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return null;
        }

        if (sourceType == targetType)
        {
            return value;
        }

        return CoerceCore(value!, sourceType, targetType);
    }

    private static object? CoerceCore(object value, Type sourceType, Type targetType)
    {
        // Guid from string or byte[]
        if (targetType == typeof(Guid))
        {
            if (value is string guidStr && Guid.TryParse(guidStr, out var guid))
            {
                return guid;
            }

            if (value is byte[] bytes && bytes.Length == 16)
            {
                return new Guid(bytes);
            }
        }

        // DateTime from string
        if (sourceType == typeof(string) && targetType == typeof(DateTime) && value is string s)
        {
            try
            {
                return DateTime.Parse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            }
            catch (Exception ex)
            {
                throw new InvalidCastException($"Cannot convert value '{value}' to type '{targetType}'.", ex);
            }
        }

        try
        {
            var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return Convert.ChangeType(value, underlyingTarget, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException($"Cannot convert value '{value}' ({sourceType}) to {targetType}.", ex);
        }
    }

    private static object? CoerceJsonValue(
        object? value,
        Type targetType,
        IColumnInfo columnInfo)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return null;
        }

        // Respect cases where the provider already gives us the desired type.
        if (value != null && targetType.IsInstanceOfType(value))
        {
            return value;
        }

        var options = columnInfo.JsonSerializerOptions ?? JsonSerializerOptions.Default;

        if (targetType == typeof(string))
        {
            return ExtractJsonString(value!, options);
        }

        return value switch
        {
            string jsonText => string.IsNullOrWhiteSpace(jsonText)
                ? null
                : JsonSerializer.Deserialize(jsonText, targetType, options),
            JsonElement element => element.Deserialize(targetType, options),
            JsonDocument document => document.Deserialize(targetType, options),
            JsonNode node => node.Deserialize(targetType, options),
            byte[] bytes => bytes.Length == 0
                ? null
                : JsonSerializer.Deserialize(bytes, targetType, options),
            ArraySegment<byte> segment when segment.Count > 0 =>
                JsonSerializer.Deserialize(new ReadOnlySpan<byte>(segment.Array!, segment.Offset, segment.Count), targetType, options),
            ReadOnlyMemory<byte> memory when !memory.IsEmpty =>
                JsonSerializer.Deserialize(memory.Span, targetType, options),
            Stream stream => DeserializeFromStream(stream, targetType, options),
            _ => RoundTripDeserialize(value!, targetType, options)
        };
    }

    private static object? RoundTripDeserialize(object value, Type targetType, JsonSerializerOptions options)
    {
        var serialized = JsonSerializer.Serialize(value, options);
        return string.IsNullOrWhiteSpace(serialized)
            ? null
            : JsonSerializer.Deserialize(serialized, targetType, options);
    }

    private static object? DeserializeFromStream(Stream stream, Type targetType, JsonSerializerOptions options)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        // JsonSerializer leaves stream positioned at the end; caller owns stream lifetime.
        return JsonSerializer.Deserialize(stream, targetType, options);
    }

    private static string ExtractJsonString(object value, JsonSerializerOptions options)
    {
        return value switch
        {
            string jsonText => jsonText,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            JsonNode node => node.ToJsonString(options),
            byte[] bytes => bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes),
            ArraySegment<byte> segment when segment.Count > 0 => Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count),
            ReadOnlyMemory<byte> memory when !memory.IsEmpty => Encoding.UTF8.GetString(memory.Span),
            Stream stream => StreamToString(stream),
            _ => JsonSerializer.Serialize(value, options)
        };
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
}
