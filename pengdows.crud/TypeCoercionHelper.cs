#region

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.types;

#endregion

namespace pengdows.crud;

public static class TypeCoercionHelper
{
    private static readonly Type GuidType = typeof(Guid);
    private static readonly Type GuidArrayType = typeof(byte[]);
    private static readonly Type ReadOnlyMemoryOfByteType = typeof(ReadOnlyMemory<byte>);
    private static readonly Type ArraySegmentOfByteType = typeof(ArraySegment<byte>);

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
        EnumParseFailureMode parseMode = EnumParseFailureMode.Throw,
        TypeCoercionOptions? options = null)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return null;
        }

        options ??= TypeCoercionOptions.Default;

        var targetType = columnInfo.PropertyInfo.PropertyType;
        var runtimeTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (runtimeTarget.IsAssignableFrom(dbFieldType) && !columnInfo.IsJsonType && columnInfo.EnumType == null)
        {
            return value;
        }

        if (columnInfo.EnumType != null)
        {
            return CoerceEnum(value!, columnInfo.EnumType, parseMode, columnInfo.PropertyInfo.PropertyType);
        }

        if (columnInfo.IsJsonType)
        {
            return CoerceJsonValue(value!, targetType, columnInfo, options);
        }

        return CoerceCore(value!, dbFieldType, targetType, options);
    }

    public static object? Coerce(
        object? value,
        Type sourceType,
        Type targetType,
        TypeCoercionOptions? options = null)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return null;
        }

        options ??= TypeCoercionOptions.Default;

        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Don't take fast path for DateTime types as they may need UTC conversion
        if (targetType.IsAssignableFrom(sourceType) && underlyingTarget != typeof(DateTime) &&
            underlyingTarget != typeof(DateTimeOffset))
        {
            return value;
        }

        if (TryGetEnumType(targetType, out var enumType))
        {
            return CoerceEnum(value!, enumType, EnumParseFailureMode.Throw, targetType);
        }

        return CoerceCore(value!, sourceType, targetType, options);
    }

    private static object? CoerceCore(object value, Type sourceType, Type targetType, TypeCoercionOptions options)
    {
        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Don't take fast path for DateTime types as they may need UTC conversion
        if (underlyingTarget.IsInstanceOfType(value) && underlyingTarget != typeof(DateTime) &&
            underlyingTarget != typeof(DateTimeOffset))
        {
            return value;
        }

        // Policy-aware type handling (DateTime/DateTimeOffset require policy context)
        if (underlyingTarget == typeof(DateTimeOffset))
        {
            return CoerceDateTimeOffset(value, options);
        }

        if (underlyingTarget == typeof(DateTime))
        {
            return CoerceDateTime(value, options);
        }

        // Primary path: Use unified CoercionRegistry system for other types
        var dbValue = new types.coercion.DbValue(value, sourceType);
        if (types.coercion.CoercionRegistry.Shared.TryRead(dbValue, underlyingTarget, out var coercedValue,
                options.Provider))
        {
            return coercedValue;
        }

        // Fallback: char[] to string conversion (not in coercion registry)
        if (underlyingTarget == typeof(string) && sourceType == typeof(char[]))
        {
            return new string((char[])value);
        }

        // Legacy path: Try advanced converter for backward compatibility
        var advancedConverter = AdvancedTypeRegistry.Shared.GetConverter(underlyingTarget);
        if (advancedConverter != null)
        {
            var converted = advancedConverter.FromProviderValue(value, options.Provider);
            if (converted != null)
            {
                return converted;
            }
        }

        // Final fallback: Convert.ChangeType
        try
        {
            return Convert.ChangeType(value, underlyingTarget, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException($"Cannot convert value '{value}' ({sourceType}) to {targetType}.", ex);
        }
    }

    private static object? CoerceEnum(object value, Type enumType, EnumParseFailureMode parseMode, Type targetType)
    {
        if (enumType.IsInstanceOfType(value))
        {
            return value;
        }

        var isNullable = Nullable.GetUnderlyingType(targetType) != null;
        var stringValue = value as string ?? (value is char c ? c.ToString() : null);

        if (!string.IsNullOrEmpty(stringValue))
        {
            if (Enum.TryParse(enumType, stringValue, true, out var parsed))
            {
                return parsed!;
            }

            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }

        try
        {
            var numeric = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            var result = Enum.ToObject(enumType, numeric);
            if (Enum.IsDefined(enumType, result))
            {
                return result;
            }

            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }
        catch
        {
            return HandleEnumFailure(value, enumType, parseMode, isNullable);
        }
    }

    private static object? HandleEnumFailure(object value, Type enumType, EnumParseFailureMode parseMode,
        bool targetNullable)
    {
        switch (parseMode)
        {
            case EnumParseFailureMode.Throw:
                throw new ArgumentException($"Cannot convert '{value}' to enum {enumType}");
            case EnumParseFailureMode.SetDefaultValue:
                return Enum.ToObject(enumType, Activator.CreateInstance(Enum.GetUnderlyingType(enumType))!);
            case EnumParseFailureMode.SetNullAndLog:
                TryLogWarning("Cannot convert '{Value}' to enum {EnumType}.", value, enumType);
                return null;
            default:
                return null;
        }
    }

    private static void TryLogWarning(string message, params object?[] args)
    {
        try
        {
            Logger.LogWarning(message, args);
        }
        catch
        {
            // Swallow logging failures to avoid breaking enum parse fallbacks.
        }
    }

    private static void TryLogDebug(string message, params object?[] args)
    {
        try
        {
            Logger.LogDebug(message, args);
        }
        catch
        {
            // Ignore logging failures in debug-only fallbacks.
        }
    }

    private static object CoerceGuid(object value)
    {
        if (value is Guid g)
        {
            return g;
        }

        if (value is string s && Guid.TryParse(s, out var parsed))
        {
            return parsed;
        }

        if (value is byte[] bytes && bytes.Length == 16)
        {
            return new Guid(bytes);
        }

        if (value is ReadOnlyMemory<byte> memory && memory.Length == 16)
        {
            return new Guid(memory.Span);
        }

        if (value is ArraySegment<byte> segment && segment.Count == 16)
        {
            return new Guid(segment.AsSpan());
        }

        if (value is char[] chars && chars.Length == 36 && Guid.TryParse(new string(chars), out var charGuid))
        {
            return charGuid;
        }

        throw new InvalidCastException($"Cannot convert value '{value}' to Guid.");
    }

    private static object CoerceBoolean(object value)
    {
        switch (value)
        {
            case bool b:
                return b;
            case string s:
                if (bool.TryParse(s, out var boolResult))
                {
                    return boolResult;
                }

                if (s.Length == 1)
                {
                    return EvaluateCharBoolean(char.ToLowerInvariant(s[0]));
                }

                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl))
                {
                    return Math.Abs(dbl) > double.Epsilon;
                }

                break;
            case char c:
                return EvaluateCharBoolean(char.ToLowerInvariant(c));
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
            case float f:
                return Math.Abs(f) > float.Epsilon;
            case double d:
                return Math.Abs(d) > double.Epsilon;
            case decimal m:
                return m != decimal.Zero;
        }

        throw new InvalidCastException($"Cannot convert value '{value}' to Boolean.");
    }

    private static bool EvaluateCharBoolean(char lower)
    {
        switch (lower)
        {
            case 't':
            case 'y':
            case '1':
                return true;
            case 'f':
            case 'n':
            case '0':
                return false;
            default:
                throw new InvalidCastException($"Cannot convert character '{lower}' to Boolean.");
        }
    }

    private static object CoerceDateTimeOffset(object value, TypeCoercionOptions options)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                return dto;
            case DateTime dt:
                return options.TimePolicy == TimeMappingPolicy.ForceUtcDateTime
                    ? new DateTimeOffset(ConvertToUtc(dt), TimeSpan.Zero)
                    : CreateFlexibleOffset(dt);
            case string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var parsed):
                return parsed;
            case string s
                when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt):
                return options.TimePolicy == TimeMappingPolicy.ForceUtcDateTime
                    ? new DateTimeOffset(ConvertToUtc(dt), TimeSpan.Zero)
                    : CreateFlexibleOffset(dt);
            default:
                throw new InvalidCastException($"Cannot convert value '{value}' to DateTimeOffset.");
        }
    }

    private static object CoerceDateTime(object value, TypeCoercionOptions options)
    {
        switch (value)
        {
            case DateTime dt:
                return DateTime.SpecifyKind(ConvertToUtc(dt), DateTimeKind.Utc);
            case DateTimeOffset dto:
                return DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Utc);
            case string s when string.IsNullOrWhiteSpace(s):
                // Treat empty/whitespace strings as invalid for DateTime
                // This handles SQLite returning empty strings for TIMESTAMP columns
                throw new InvalidCastException($"Cannot convert value '{value}' to DateTime.");
            case string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var dto):
                return DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Utc);
            case string s
                when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt):
                return DateTime.SpecifyKind(ConvertToUtc(dt), DateTimeKind.Utc);
            default:
                throw new InvalidCastException($"Cannot convert value '{value}' to DateTime.");
        }
    }

    private static DateTime ConvertToUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

    private static DateTimeOffset CreateFlexibleOffset(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        if (dt.Kind == DateTimeKind.Local)
        {
            return new DateTimeOffset(dt);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero);
    }

    private static bool TryGetEnumType(Type targetType, out Type enumType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsEnum)
        {
            enumType = underlying;
            return true;
        }

        enumType = underlying;
        return false;
    }

    private static object? CoerceJsonValue(
        object value,
        Type targetType,
        IColumnInfo columnInfo,
        TypeCoercionOptions options)
    {
        var actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualTarget.IsInstanceOfType(value))
        {
            return value;
        }

        var serializerOptions = columnInfo.JsonSerializerOptions ?? JsonSerializerOptions.Default;

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

        if (actualTarget == typeof(JsonNode))
        {
            var text = ExtractJsonString(value, serializerOptions);
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text, new JsonNodeOptions());
        }

        var jsonText = ExtractJsonString(value, serializerOptions);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            if (value is Stream)
            {
                throw new JsonException("JSON payload cannot be empty.");
            }

            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(jsonText, actualTarget, serializerOptions);
        }
        catch (JsonException) when (value is not Stream)
        {
            TryLogDebug("Failed to deserialize JSON value '{Value}' into {TargetType}", value, actualTarget);
            return null;
        }
    }

    private static JsonDocument ToJsonDocument(object value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case JsonDocument doc:
                return doc;
            case JsonElement element:
                return JsonDocument.Parse(element.GetRawText());
            case JsonNode node:
                return JsonDocument.Parse(node.ToJsonString(options));
            case string s:
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(s) ? "null" : s);
            case byte[] bytes when bytes.Length == 0:
                return JsonDocument.Parse("null");
            case byte[] bytes:
                return JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            case ArraySegment<byte> segment when segment.Count > 0:
                return JsonDocument.Parse(Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count));
            case ReadOnlyMemory<byte> memory when !memory.IsEmpty:
                return JsonDocument.Parse(Encoding.UTF8.GetString(memory.Span));
            case Stream stream:
                return DeserializeStreamToDocument(stream);
            case char[] chars:
                return JsonDocument.Parse(new string(chars));
            default:
                var serialized = JsonSerializer.Serialize(value, options);
                return JsonDocument.Parse(serialized);
        }
    }

    private static JsonDocument DeserializeStreamToDocument(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var text = reader.ReadToEnd();
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
    }

    private static string ExtractJsonString(object value, JsonSerializerOptions options)
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
            case ArraySegment<byte> segment when segment.Count > 0:
                return Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count);
            case ReadOnlyMemory<byte> memory when !memory.IsEmpty:
                return Encoding.UTF8.GetString(memory.Span);
            case Stream stream:
                return StreamToString(stream);
            case char[] chars:
                return new string(chars);
            default:
                return JsonSerializer.Serialize(value, options);
        }
    }

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
}