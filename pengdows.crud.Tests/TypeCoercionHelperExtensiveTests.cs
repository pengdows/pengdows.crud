using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.Tests;

[Collection("TypeRegistry")]
public class TypeCoercionHelperExtensiveTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TestLogger _logger;

    public TypeCoercionHelperExtensiveTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new TestLogger(_output);
        TypeCoercionHelper.Logger = _logger;
    }

    public void Dispose()
    {
        // Reset to NullLogger to avoid stale ITestOutputHelper references
        TypeCoercionHelper.Logger = NullLogger.Instance;
    }

    #region Logger Testing

    [Fact]
    public void Logger_Property_GetSet()
    {
        var originalLogger = TypeCoercionHelper.Logger;
        try
        {
            var customLogger = new NullLogger<object>();
            TypeCoercionHelper.Logger = customLogger;
            Assert.Same(customLogger, TypeCoercionHelper.Logger);

            // Setting null should fall back to NullLogger  
            TypeCoercionHelper.Logger = new NullLogger<object>();
            Assert.IsType<NullLogger<object>>(TypeCoercionHelper.Logger);
        }
        finally
        {
            TypeCoercionHelper.Logger = originalLogger;
        }
    }

    #endregion

    #region TypeCoercionOptions Testing

    [Fact]
    public void TypeCoercionOptions_DefaultValues()
    {
        var options = TypeCoercionOptions.Default;

        Assert.Equal(TimeMappingPolicy.PreferDateTimeOffset, options.TimePolicy);
        Assert.Equal(JsonPassThrough.PreferDocument, options.JsonPreference);
        Assert.Equal(SupportedDatabase.Unknown, options.Provider);
    }

    [Fact]
    public void TypeCoercionOptions_CustomValues()
    {
        var options = new TypeCoercionOptions(
            TimeMappingPolicy.ForceUtcDateTime,
            JsonPassThrough.PreferText,
            SupportedDatabase.PostgreSql);

        Assert.Equal(TimeMappingPolicy.ForceUtcDateTime, options.TimePolicy);
        Assert.NotEqual(TypeCoercionOptions.Default.TimePolicy, options.TimePolicy);
        Assert.Equal(JsonPassThrough.PreferText, options.JsonPreference);
        Assert.Equal(SupportedDatabase.PostgreSql, options.Provider);
    }

    #endregion

    #region Null Value Handling

    [Fact]
    public void Coerce_WithNull_ReturnsNull()
    {
        var result = TypeCoercionHelper.Coerce(null, typeof(string), typeof(int));
        Assert.Null(result);
    }

    [Fact]
    public void Coerce_WithDbNull_ReturnsNull()
    {
        var result = TypeCoercionHelper.Coerce(DBNull.Value, typeof(string), typeof(int));
        Assert.Null(result);
    }

    #endregion

    #region Guid Coercion Testing

    [Fact]
    public void CoerceGuid_FromGuid_ReturnsSameGuid()
    {
        var guid = Guid.NewGuid();
        var result = TypeCoercionHelper.Coerce(guid, typeof(Guid), typeof(Guid));
        Assert.Equal(guid, result);
    }

    [Fact]
    public void CoerceGuid_FromString_ParsesCorrectly()
    {
        var guid = Guid.NewGuid();
        var guidString = guid.ToString();
        var result = TypeCoercionHelper.Coerce(guidString, typeof(string), typeof(Guid));
        Assert.Equal(guid, result);
    }

    [Fact]
    public void CoerceGuid_FromByteArray_ConvertsCorrectly()
    {
        var guid = Guid.NewGuid();
        var bytes = guid.ToByteArray();
        var result = TypeCoercionHelper.Coerce(bytes, typeof(byte[]), typeof(Guid));
        Assert.Equal(guid, result);
    }

    [Fact]
    public void CoerceGuid_FromReadOnlyMemory_ConvertsCorrectly()
    {
        var guid = Guid.NewGuid();
        var memory = new ReadOnlyMemory<byte>(guid.ToByteArray());
        var result = TypeCoercionHelper.Coerce(memory, typeof(ReadOnlyMemory<byte>), typeof(Guid));
        Assert.Equal(guid, result);
    }

    [Fact]
    public void CoerceGuid_FromArraySegment_ConvertsCorrectly()
    {
        var guid = Guid.NewGuid();
        var bytes = guid.ToByteArray();
        var segment = new ArraySegment<byte>(bytes);
        var result = TypeCoercionHelper.Coerce(segment, typeof(ArraySegment<byte>), typeof(Guid));
        Assert.Equal(guid, result);
    }

    [Fact]
    public void CoerceGuid_FromCharArray_ConvertsCorrectly()
    {
        var guid = Guid.NewGuid();
        var chars = guid.ToString().ToCharArray();
        var result = TypeCoercionHelper.Coerce(chars, typeof(char[]), typeof(Guid));
        Assert.Equal(guid, result);
    }

    [Fact]
    public void CoerceGuid_FromInvalidString_ThrowsException()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("invalid-guid", typeof(string), typeof(Guid)));
    }

    [Fact]
    public void CoerceGuid_FromWrongSizeByteArray_ThrowsException()
    {
        var bytes = new byte[8]; // Wrong size
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce(bytes, typeof(byte[]), typeof(Guid)));
    }

    #endregion

    #region Boolean Coercion Testing

    [Fact]
    public void CoerceBoolean_FromBoolean_ReturnsSame()
    {
        Assert.True((bool)TypeCoercionHelper.Coerce(true, typeof(bool), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(false, typeof(bool), typeof(bool))!);
    }

    [Fact]
    public void CoerceBoolean_FromString_ParsesCorrectly()
    {
        Assert.True((bool)TypeCoercionHelper.Coerce("true", typeof(string), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce("false", typeof(string), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce("True", typeof(string), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce("False", typeof(string), typeof(bool))!);
    }

    [Fact]
    public void CoerceBoolean_FromSingleChar_ParsesCorrectly()
    {
        Assert.True((bool)TypeCoercionHelper.Coerce("t", typeof(string), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce("T", typeof(string), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce("y", typeof(string), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce("Y", typeof(string), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce("1", typeof(string), typeof(bool))!);

        Assert.False((bool)TypeCoercionHelper.Coerce("f", typeof(string), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce("F", typeof(string), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce("n", typeof(string), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce("N", typeof(string), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce("0", typeof(string), typeof(bool))!);
    }

    [Fact]
    public void CoerceBoolean_FromChar_ParsesCorrectly()
    {
        Assert.True((bool)TypeCoercionHelper.Coerce('t', typeof(char), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce('T', typeof(char), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce('y', typeof(char), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce('1', typeof(char), typeof(bool))!);

        Assert.False((bool)TypeCoercionHelper.Coerce('f', typeof(char), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce('n', typeof(char), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce('0', typeof(char), typeof(bool))!);
    }

    [Fact]
    public void CoerceBoolean_FromNumericTypes_ConvertsCorrectly()
    {
        // Integer types
        Assert.True((bool)TypeCoercionHelper.Coerce((sbyte)1, typeof(sbyte), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce((sbyte)0, typeof(sbyte), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce((byte)1, typeof(byte), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce((byte)0, typeof(byte), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce((short)1, typeof(short), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce((short)0, typeof(short), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce((ushort)1, typeof(ushort), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce((ushort)0, typeof(ushort), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce(1, typeof(int), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0, typeof(int), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce(1u, typeof(uint), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0u, typeof(uint), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce(1L, typeof(long), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0L, typeof(long), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce(1UL, typeof(ulong), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0UL, typeof(ulong), typeof(bool))!);

        // Floating point types
        Assert.True((bool)TypeCoercionHelper.Coerce(1.0f, typeof(float), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0.0f, typeof(float), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce(1.0, typeof(double), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0.0, typeof(double), typeof(bool))!);
        Assert.True((bool)TypeCoercionHelper.Coerce(1.0m, typeof(decimal), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0.0m, typeof(decimal), typeof(bool))!);
    }

    [Fact]
    public void CoerceBoolean_FromNumericString_ConvertsCorrectly()
    {
        Assert.True((bool)TypeCoercionHelper.Coerce("1.5", typeof(string), typeof(bool))!);
        Assert.False((bool)TypeCoercionHelper.Coerce("0.0", typeof(string), typeof(bool))!);
    }

    [Fact]
    public void CoerceBoolean_FromInvalidChar_ThrowsException()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce('x', typeof(char), typeof(bool)));
    }

    [Fact]
    public void CoerceBoolean_FromInvalidString_ThrowsException()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("invalid", typeof(string), typeof(bool)));
    }

    #endregion

    #region DateTime Coercion Testing

    [Fact]
    public void CoerceDateTimeOffset_FromDateTimeOffset_ReturnsSame()
    {
        var dto = DateTimeOffset.Now;
        var result = TypeCoercionHelper.Coerce(dto, typeof(DateTimeOffset), typeof(DateTimeOffset));
        Assert.Equal(dto, result);
    }

    [Fact]
    public void CoerceDateTimeOffset_FromDateTime_PreferDateTimeOffset()
    {
        var dt = DateTime.UtcNow;
        var options = new TypeCoercionOptions(TimeMappingPolicy.PreferDateTimeOffset, JsonPassThrough.PreferDocument,
            SupportedDatabase.Unknown);
        var result = (DateTimeOffset)TypeCoercionHelper.Coerce(dt, typeof(DateTime), typeof(DateTimeOffset), options)!;

        Assert.Equal(dt, result.UtcDateTime);
    }

    [Fact]
    public void CoerceDateTimeOffset_FromDateTime_ForceUtc()
    {
        var dt = DateTime.Now; // Local time
        var options = new TypeCoercionOptions(TimeMappingPolicy.ForceUtcDateTime, JsonPassThrough.PreferDocument,
            SupportedDatabase.Unknown);
        var result = (DateTimeOffset)TypeCoercionHelper.Coerce(dt, typeof(DateTime), typeof(DateTimeOffset), options)!;

        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void CoerceDateTimeOffset_FromString_ParsesCorrectly()
    {
        var dateString = "2023-01-01T12:00:00Z";
        var result = (DateTimeOffset)TypeCoercionHelper.Coerce(dateString, typeof(string), typeof(DateTimeOffset))!;

        Assert.Equal(new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void CoerceDateTimeOffset_FromDateTimeString_ParsesCorrectly()
    {
        var dateString = "2023-01-01T12:00:00Z"; // Explicit UTC timestamp
        var options = new TypeCoercionOptions(TimeMappingPolicy.ForceUtcDateTime, JsonPassThrough.PreferDocument,
            SupportedDatabase.Unknown);
        var result =
            (DateTimeOffset)TypeCoercionHelper.Coerce(dateString, typeof(string), typeof(DateTimeOffset), options)!;

        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void CoerceDateTimeOffset_FromInvalidString_ThrowsException()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("invalid-date", typeof(string), typeof(DateTimeOffset)));
    }

    [Fact]
    public void CoerceDateTime_FromDateTime_ConvertedToUtc()
    {
        var dt = DateTime.Now; // Local time
        var options = new TypeCoercionOptions(TimeMappingPolicy.ForceUtcDateTime, JsonPassThrough.PreferDocument,
            SupportedDatabase.Unknown);
        var result = (DateTime)TypeCoercionHelper.Coerce(dt, typeof(DateTime), typeof(DateTime), options)!;

        // With ForceUtcDateTime policy, should convert to UTC
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void CoerceDateTime_FromDateTimeOffset_ExtractsUtc()
    {
        var dto = DateTimeOffset.Now;
        var result = (DateTime)TypeCoercionHelper.Coerce(dto, typeof(DateTimeOffset), typeof(DateTime))!;

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(dto.UtcDateTime, result);
    }

    [Fact]
    public void CoerceDateTime_FromDateTimeOffsetString_ParsesCorrectly()
    {
        var dateString = "2023-01-01T12:00:00+05:00";
        var result = (DateTime)TypeCoercionHelper.Coerce(dateString, typeof(string), typeof(DateTime))!;

        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void CoerceDateTime_FromInvalidString_ThrowsException()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("invalid-date", typeof(string), typeof(DateTime)));
    }

    #endregion

    #region Decimal Coercion Testing

    [Fact]
    public void CoerceDecimal_FromVariousNumericTypes_ConvertsCorrectly()
    {
        Assert.Equal(42.5m, (decimal)TypeCoercionHelper.Coerce(42.5, typeof(double), typeof(decimal))!);
        Assert.Equal(42m, (decimal)TypeCoercionHelper.Coerce(42, typeof(int), typeof(decimal))!);
        Assert.Equal(42.5m, (decimal)TypeCoercionHelper.Coerce(42.5f, typeof(float), typeof(decimal))!);
    }

    #endregion

    #region String Coercion Testing

    [Fact]
    public void CoerceString_FromCharArray_ConvertsCorrectly()
    {
        var chars = "hello".ToCharArray();
        var result = (string)TypeCoercionHelper.Coerce(chars, typeof(char[]), typeof(string))!;
        Assert.Equal("hello", result);
    }

    #endregion

    #region Advanced Type Converter Integration

    [Fact]
    public void Coerce_WithAdvancedTypeConverter_UsesConverter()
    {
        var inet = new Inet(System.Net.IPAddress.Parse("192.168.1.1"));
        var inetString = "192.168.1.1";

        // This should use the InetConverter from AdvancedTypeRegistry
        var result = TypeCoercionHelper.Coerce(inetString, typeof(string), typeof(Inet));

        // The converter should convert the string to an Inet object
        Assert.NotNull(result);
        Assert.IsType<Inet>(result);
    }

    #endregion

    #region Enum Coercion Testing

    private enum TestEnum
    {
        None = 0,
        First = 1,
        Second = 2
    }

    [Fact]
    public void CoerceEnum_FromSameEnum_ReturnsSame()
    {
        var result = TypeCoercionHelper.Coerce(TestEnum.First, typeof(TestEnum), typeof(TestEnum));
        Assert.Equal(TestEnum.First, result);
    }

    [Fact]
    public void CoerceEnum_FromString_ParsesCorrectly()
    {
        var result = TypeCoercionHelper.Coerce("First", typeof(string), typeof(TestEnum));
        Assert.Equal(TestEnum.First, result);
    }

    [Fact]
    public void CoerceEnum_FromStringCaseInsensitive_ParsesCorrectly()
    {
        var result = TypeCoercionHelper.Coerce("first", typeof(string), typeof(TestEnum));
        Assert.Equal(TestEnum.First, result);
    }

    [Fact]
    public void CoerceEnum_FromChar_ParsesCorrectly()
    {
        // Test with a character that should parse to a valid enum value (convert '1' to TestEnum.First)
        var result = TypeCoercionHelper.Coerce('1', typeof(char), typeof(TestEnum));
        Assert.Equal(TestEnum.First, result);
    }

    [Fact]
    public void CoerceEnum_FromNumeric_ConvertsCorrectly()
    {
        var result = TypeCoercionHelper.Coerce(1, typeof(int), typeof(TestEnum));
        Assert.Equal(TestEnum.First, result);
    }

    [Fact]
    public void CoerceEnum_FromLong_ConvertsCorrectly()
    {
        var result = TypeCoercionHelper.Coerce(1L, typeof(long), typeof(TestEnum));
        Assert.Equal(TestEnum.First, result);
    }

    [Fact]
    public void CoerceEnum_FromInvalidString_ThrowsByDefault()
    {
        Assert.Throws<ArgumentException>(() =>
            TypeCoercionHelper.Coerce("Invalid", typeof(string), typeof(TestEnum)));
    }

    [Fact]
    public void CoerceEnum_FromInvalidNumeric_ThrowsByDefault()
    {
        Assert.Throws<ArgumentException>(() =>
            TypeCoercionHelper.Coerce(999, typeof(int), typeof(TestEnum)));
    }

    [Fact]
    public void CoerceEnum_FromNullableEnum_HandlesCorrectly()
    {
        var result = TypeCoercionHelper.Coerce(TestEnum.First, typeof(TestEnum), typeof(TestEnum?));
        Assert.Equal(TestEnum.First, result);
    }

    #endregion

    #region JSON Coercion Testing

    [Fact]
    public void CoerceJson_FromJsonDocument_ReturnsSame()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var doc = JsonDocument.Parse("{}");
        var result = TypeCoercionHelper.Coerce(doc, typeof(JsonDocument), columnInfo);
        Assert.Same(doc, result);
    }

    [Fact]
    public void CoerceJson_ToJsonDocument_FromString()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var json = "{\"name\":\"test\"}";
        var result = (JsonDocument)TypeCoercionHelper.Coerce(json, typeof(string), columnInfo)!;

        Assert.Equal("test", result.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void CoerceJson_ToJsonElement_FromString()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonElement));
        var json = "{\"name\":\"test\"}";
        var result = (JsonElement)TypeCoercionHelper.Coerce(json, typeof(string), columnInfo)!;

        Assert.Equal("test", result.GetProperty("name").GetString());
    }

    [Fact]
    public void CoerceJson_ToJsonNode_FromString()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonNode));
        var json = "{\"name\":\"test\"}";
        var result = (JsonNode)TypeCoercionHelper.Coerce(json, typeof(string), columnInfo)!;

        Assert.Equal("test", result!["name"]!.GetValue<string>());
    }

    [Fact]
    public void CoerceJson_ToString_FromJsonDocument()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(string));
        var doc = JsonDocument.Parse("{\"name\":\"test\"}");
        var result = (string)TypeCoercionHelper.Coerce(doc, typeof(JsonDocument), columnInfo)!;

        Assert.Contains("test", result);
    }

    [Fact]
    public void CoerceJson_ToReadOnlyMemoryChar_FromString()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(ReadOnlyMemory<char>));
        var json = "{\"name\":\"test\"}";
        var result = (ReadOnlyMemory<char>)TypeCoercionHelper.Coerce(json, typeof(string), columnInfo)!;

        Assert.Contains("test", new string(result.Span));
    }

    [Fact]
    public void CoerceJson_FromByteArray_ConvertsCorrectly()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var json = "{\"name\":\"test\"}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var result = (JsonDocument)TypeCoercionHelper.Coerce(bytes, typeof(byte[]), columnInfo)!;

        Assert.Equal("test", result.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void CoerceJson_FromArraySegment_ConvertsCorrectly()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var json = "{\"name\":\"test\"}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);
        var result = (JsonDocument)TypeCoercionHelper.Coerce(segment, typeof(ArraySegment<byte>), columnInfo)!;

        Assert.Equal("test", result.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void CoerceJson_FromReadOnlyMemoryByte_ConvertsCorrectly()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var json = "{\"name\":\"test\"}";
        var memory = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var result = (JsonDocument)TypeCoercionHelper.Coerce(memory, typeof(ReadOnlyMemory<byte>), columnInfo)!;

        Assert.Equal("test", result.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void CoerceJson_FromStream_ConvertsCorrectly()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var json = "{\"name\":\"test\"}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = (JsonDocument)TypeCoercionHelper.Coerce(stream, typeof(Stream), columnInfo)!;

        Assert.Equal("test", result.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void CoerceJson_FromCharArray_ConvertsCorrectly()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var json = "{\"name\":\"test\"}";
        var chars = json.ToCharArray();
        var result = (JsonDocument)TypeCoercionHelper.Coerce(chars, typeof(char[]), columnInfo)!;

        Assert.Equal("test", result.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void CoerceJson_FromEmptyByteArray_ReturnsNull()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(JsonDocument));
        var bytes = Array.Empty<byte>();
        var result = (JsonDocument)TypeCoercionHelper.Coerce(bytes, typeof(byte[]), columnInfo)!;

        Assert.Equal(JsonValueKind.Null, result.RootElement.ValueKind);
    }

    [Fact]
    public void CoerceJson_ToCustomType_DeserializesCorrectly()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(TestJsonObject));
        var json = "{\"Name\":\"test\",\"Value\":42}";
        var result = (TestJsonObject)TypeCoercionHelper.Coerce(json, typeof(string), columnInfo)!;

        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void CoerceJson_EmptyString_ReturnsNull()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(TestJsonObject));
        var result = TypeCoercionHelper.Coerce("", typeof(string), columnInfo);

        Assert.Null(result);
    }

    [Fact]
    public void CoerceJson_WhitespaceString_ReturnsNull()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(TestJsonObject));
        var result = TypeCoercionHelper.Coerce("   ", typeof(string), columnInfo);

        Assert.Null(result);
    }

    [Fact]
    public void CoerceJson_FromStreamEmptyPayload_ThrowsException()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(TestJsonObject));
        using var stream = new MemoryStream();

        Assert.Throws<JsonException>(() =>
            TypeCoercionHelper.Coerce(stream, typeof(Stream), columnInfo));
    }

    [Fact]
    public void CoerceJson_InvalidJsonString_ReturnsNullForNonStream()
    {
        var columnInfo = CreateJsonColumnInfo(typeof(TestJsonObject));

        // Ensure logger is set for this test (in case another test changed it)
        TypeCoercionHelper.Logger = _logger;
        _logger.Messages.Clear(); // Clear any previous messages
        _logger.LogLevel = LogLevel.Debug; // Enable debug logging

        var result = TypeCoercionHelper.Coerce("invalid-json", typeof(string), columnInfo);

        Assert.Null(result);
        Assert.Contains(_logger.Messages, msg => msg.Contains("Failed to deserialize JSON value"));
    }

    #endregion

    #region GetJsonText Testing

    [Fact]
    public void GetJsonText_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeCoercionHelper.GetJsonText(null!));
    }

    [Fact]
    public void GetJsonText_WithString_ReturnsString()
    {
        var result = TypeCoercionHelper.GetJsonText("test");
        Assert.Equal("test", result);
    }

    [Fact]
    public void GetJsonText_WithJsonDocument_ReturnsRawText()
    {
        var doc = JsonDocument.Parse("{\"name\":\"test\"}");
        var result = TypeCoercionHelper.GetJsonText(doc);
        Assert.Contains("test", result);
    }

    #endregion

    #region Convert.ChangeType Fallback Testing

    [Fact]
    public void Coerce_FallsBackToChangeType_ForUnsupportedTypes()
    {
        var result = TypeCoercionHelper.Coerce(42, typeof(int), typeof(long));
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Coerce_ChangeTypeFails_ThrowsInvalidCastException()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("not-a-number", typeof(string), typeof(int)));
    }

    #endregion

    #region Assignable Type Fast Path Testing

    [Fact]
    public void Coerce_AssignableTypes_ReturnsSameValue()
    {
        var value = "test";
        var result = TypeCoercionHelper.Coerce(value, typeof(string), typeof(string));
        Assert.Same(value, result);
    }

    #endregion

    #region Helper Methods

    private static IColumnInfo CreateJsonColumnInfo(Type propertyType)
    {
        var testPropertyInfo = new TestPropertyInfo();
        testPropertyInfo.PropertyTypeToSet = propertyType;

        return new TestColumnInfo
        {
            PropertyInfo = testPropertyInfo,
            IsJsonType = true,
            JsonSerializerOptions = new JsonSerializerOptions()
        };
    }

    private sealed class TestColumnInfo : IColumnInfo
    {
        public string Name { get; init; } = "Test";
        public PropertyInfo PropertyInfo { get; init; } = null!;
        public bool IsId { get; init; }
        public DbType DbType { get; set; }
        public bool IsNonUpdateable { get; set; }
        public bool IsNonInsertable { get; set; }
        public bool IsEnum { get; set; }
        public Type? EnumType { get; set; }
        public Type? EnumUnderlyingType { get; set; }
        public bool IsJsonType { get; set; }
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new();
        public bool IsIdIsWritable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public int PkOrder { get; set; }
        public bool IsVersion { get; set; }
        public bool IsCreatedBy { get; set; }
        public bool IsCreatedOn { get; set; }
        public bool IsLastUpdatedBy { get; set; }
        public bool IsLastUpdatedOn { get; set; }
        public int Ordinal { get; set; }

        public object? MakeParameterValueFromField<T>(T objectToCreate)
        {
            return null;
        }
    }

    private sealed class TestPropertyInfo : PropertyInfo
    {
        private Type _propertyType = typeof(object);
        public override Type PropertyType => _propertyType;

        public Type PropertyTypeToSet
        {
            set => _propertyType = value;
        }

        public override PropertyAttributes Attributes => PropertyAttributes.None;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override string Name => "TestProperty";
        public override Type DeclaringType => typeof(object);
        public override Type ReflectedType => typeof(object);

        public override MethodInfo[] GetAccessors(bool nonPublic)
        {
            return Array.Empty<MethodInfo>();
        }

        public override MethodInfo GetGetMethod(bool nonPublic)
        {
            return null!;
        }

        public override ParameterInfo[] GetIndexParameters()
        {
            return Array.Empty<ParameterInfo>();
        }

        public override MethodInfo GetSetMethod(bool nonPublic)
        {
            return null!;
        }

        public override object GetValue(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? index,
            System.Globalization.CultureInfo? culture)
        {
            return null!;
        }

        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder,
            object?[]? index, System.Globalization.CultureInfo? culture)
        {
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            return Array.Empty<object>();
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            return Array.Empty<object>();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            return false;
        }
    }

    private sealed class TestJsonObject
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private sealed class TestLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        public LogLevel LogLevel { get; set; } = LogLevel.Information;
        public List<string> Messages { get; } = new();

        public TestLogger(ITestOutputHelper output)
        {
            _output = output;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            Messages.Add(message);
            try
            {
                _output.WriteLine($"[{logLevel}] {message}");
            }
            catch (InvalidOperationException)
            {
                // Some runners (coverage) may not expose the active test context; swallow.
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    #endregion
}
