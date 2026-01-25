using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests;

public class TypeCoercionHelperBranchTests
{
    private enum SampleEnum
    {
        First = 1,
        Second = 2
    }

    private sealed class SampleHolder
    {
        public string? Name { get; set; }
        public SampleEnum EnumValue { get; set; }
        public JsonNode? Node { get; set; }
    }

    private sealed class TestColumnInfo : IColumnInfo
    {
        public string Name { get; init; } = "Test";
        public PropertyInfo PropertyInfo { get; init; } = typeof(SampleHolder).GetProperty(nameof(SampleHolder.Name))!;
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
        public object? MakeParameterValueFromField<T>(T objectToCreate) => null;
    }

    private sealed class TestPayload
    {
        public TestPayload(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }

    private sealed class TestPayloadConverter : AdvancedTypeConverter<TestPayload>
    {
        public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out TestPayload result)
        {
            if (value is string text)
            {
                result = new TestPayload(text);
                return true;
            }

            result = default!;
            return false;
        }
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(string text)
        {
            _inner = new MemoryStream(Encoding.UTF8.GetBytes(text));
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static T InvokePrivate<T>(string name, params object?[] args)
    {
        var method = typeof(TypeCoercionHelper).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    private static Exception InvokePrivateThrows(string name, params object?[] args)
    {
        var method = typeof(TypeCoercionHelper).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, args));
        return ex.InnerException!;
    }

    [Fact]
    public void Logger_NullValue_UsesNullLogger()
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = null!;
            Assert.Same(NullLogger.Instance, TypeCoercionHelper.Logger);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public void Coerce_ReturnsAssignableValueWhenNoJsonOrEnum()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = typeof(SampleHolder).GetProperty(nameof(SampleHolder.Name))!,
            IsJsonType = false,
            EnumType = null
        };

        var value = "unchanged";
        var result = TypeCoercionHelper.Coerce(value, typeof(string), column);

        Assert.Same(value, result);
    }

    [Fact]
    public void Coerce_WithEnumValue_PassesThroughInstance()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = typeof(SampleHolder).GetProperty(nameof(SampleHolder.EnumValue))!,
            IsEnum = true,
            EnumType = typeof(SampleEnum)
        };

        var value = SampleEnum.Second;
        var result = TypeCoercionHelper.Coerce(value, typeof(SampleEnum), column, EnumParseFailureMode.Throw);

        Assert.Equal(value, result);
    }

    [Fact]
    public void Coerce_WithNullValue_ReturnsNull()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = typeof(SampleHolder).GetProperty(nameof(SampleHolder.Name))!,
            IsJsonType = false,
            EnumType = null
        };

        Assert.Null(TypeCoercionHelper.Coerce(null, typeof(string), column));
        Assert.Null(TypeCoercionHelper.Coerce(DBNull.Value, typeof(string), column));
    }

    [Fact]
    public void Coerce_UsesAdvancedConverterFallback()
    {
        AdvancedTypeRegistry.Shared.RegisterConverter(new TestPayloadConverter());

        var converted = (TestPayload)TypeCoercionHelper.Coerce("payload", typeof(string), typeof(TestPayload))!;
        Assert.Equal("payload", converted.Value);

        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce(123, typeof(int), typeof(TestPayload)));
    }

    [Fact]
    public void CoerceEnum_InvalidParseMode_ReturnsNull()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = typeof(SampleHolder).GetProperty(nameof(SampleHolder.EnumValue))!,
            IsEnum = true,
            EnumType = typeof(SampleEnum)
        };

        var result = TypeCoercionHelper.Coerce("Unknown", typeof(string), column, (EnumParseFailureMode)99);
        Assert.Null(result);
    }

    [Fact]
    public void CoerceGuid_PrivateHandlesSupportedShapes()
    {
        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        Assert.Equal(guid, InvokePrivate<Guid>("CoerceGuid", guid));
        Assert.Equal(guid, InvokePrivate<Guid>("CoerceGuid", guid.ToString()));
        Assert.Equal(guid, InvokePrivate<Guid>("CoerceGuid", guid.ToByteArray()));
        Assert.Equal(guid, InvokePrivate<Guid>("CoerceGuid", new ReadOnlyMemory<byte>(guid.ToByteArray())));
        Assert.Equal(guid, InvokePrivate<Guid>("CoerceGuid", new ArraySegment<byte>(guid.ToByteArray())));
        Assert.Equal(guid, InvokePrivate<Guid>("CoerceGuid", guid.ToString().ToCharArray()));

        var ex = InvokePrivateThrows("CoerceGuid", "not-a-guid");
        Assert.IsType<InvalidCastException>(ex);
    }

    [Fact]
    public void CoerceBoolean_PrivateHandlesMultiplePaths()
    {
        Assert.True(InvokePrivate<bool>("CoerceBoolean", true));
        Assert.True(InvokePrivate<bool>("CoerceBoolean", "true"));
        Assert.True(InvokePrivate<bool>("CoerceBoolean", "y"));
        Assert.True(InvokePrivate<bool>("CoerceBoolean", "1.5"));
        Assert.True(InvokePrivate<bool>("CoerceBoolean", (byte)1));
        Assert.False(InvokePrivate<bool>("CoerceBoolean", (ushort)0));
        Assert.True(InvokePrivate<bool>("CoerceBoolean", (uint)1));
        Assert.False(InvokePrivate<bool>("CoerceBoolean", (ulong)0));
        Assert.True(InvokePrivate<bool>("CoerceBoolean", 1.0m));
        Assert.False(InvokePrivate<bool>("CoerceBoolean", 0.0f));
        Assert.False(InvokePrivate<bool>("CoerceBoolean", 0));

        var ex = InvokePrivateThrows("CoerceBoolean", "not-a-bool");
        Assert.IsType<InvalidCastException>(ex);
    }

    [Fact]
    public void EvaluateCharBoolean_PrivateThrowsOnUnknown()
    {
        Assert.True(InvokePrivate<bool>("EvaluateCharBoolean", 't'));
        Assert.False(InvokePrivate<bool>("EvaluateCharBoolean", 'f'));
        Assert.False(InvokePrivate<bool>("EvaluateCharBoolean", 'n'));
        Assert.False(InvokePrivate<bool>("EvaluateCharBoolean", '0'));

        var ex = InvokePrivateThrows("EvaluateCharBoolean", 'x');
        Assert.IsType<InvalidCastException>(ex);
    }

    [Fact]
    public void CoerceDateTimeOffset_PrivateHandlesInputs()
    {
        var options = TypeCoercionOptions.Default;
        var forceOptions = new TypeCoercionOptions(TimeMappingPolicy.ForceUtcDateTime, JsonPassThrough.PreferDocument, SupportedDatabase.Unknown);
        var dto = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(dto, InvokePrivate<DateTimeOffset>("CoerceDateTimeOffset", dto, options));

        var utcDateTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var forced = InvokePrivate<DateTimeOffset>("CoerceDateTimeOffset", utcDateTime, forceOptions);
        Assert.Equal(TimeSpan.Zero, forced.Offset);

        var parsed = InvokePrivate<DateTimeOffset>("CoerceDateTimeOffset", "2024-01-01T12:00:00+00:00", options);
        Assert.Equal(dto, parsed);

        var ex = InvokePrivateThrows("CoerceDateTimeOffset", "not-a-date", options);
        Assert.IsType<InvalidCastException>(ex);
    }

    [Fact]
    public void CoerceDateTime_PrivateHandlesInputs()
    {
        var options = TypeCoercionOptions.Default;
        var dt = new DateTime(2024, 2, 1, 8, 0, 0, DateTimeKind.Local);
        var dto = new DateTimeOffset(2024, 2, 1, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(DateTimeKind.Utc, InvokePrivate<DateTime>("CoerceDateTime", dt, options).Kind);
        Assert.Equal(DateTimeKind.Utc, InvokePrivate<DateTime>("CoerceDateTime", dto, options).Kind);

        var ex = InvokePrivateThrows("CoerceDateTime", "   ", options);
        Assert.IsType<InvalidCastException>(ex);

        var parsed = InvokePrivate<DateTime>("CoerceDateTime", "2024-02-01T08:00:00+00:00", options);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    [Fact]
    public void ConvertToUtc_PrivateCoversKinds()
    {
        var utc = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var local = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(DateTimeKind.Utc, InvokePrivate<DateTime>("ConvertToUtc", utc).Kind);
        Assert.Equal(DateTimeKind.Utc, InvokePrivate<DateTime>("ConvertToUtc", local).Kind);
        Assert.Equal(DateTimeKind.Utc, InvokePrivate<DateTime>("ConvertToUtc", unspecified).Kind);
    }

    [Fact]
    public void CreateFlexibleOffset_PrivateCoversKinds()
    {
        var utc = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var local = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(TimeSpan.Zero, InvokePrivate<DateTimeOffset>("CreateFlexibleOffset", utc).Offset);
        var expectedLocalOffset = TimeZoneInfo.Local.GetUtcOffset(local);
        Assert.Equal(expectedLocalOffset, InvokePrivate<DateTimeOffset>("CreateFlexibleOffset", local).Offset);
        Assert.Equal(TimeSpan.Zero, InvokePrivate<DateTimeOffset>("CreateFlexibleOffset", unspecified).Offset);
    }

    [Fact]
    public void TryGetEnumType_PrivateReportsEnum()
    {
        var enumArgs = new object?[] { typeof(SampleEnum), null };
        var result = InvokePrivate<bool>("TryGetEnumType", enumArgs);
        var enumType = (Type)enumArgs[1]!;
        Assert.True(result);
        Assert.Equal(typeof(SampleEnum), enumType);

        var nonEnumArgs = new object?[] { typeof(string), null };
        var nonEnum = InvokePrivate<bool>("TryGetEnumType", nonEnumArgs);
        var otherType = (Type)nonEnumArgs[1]!;
        Assert.False(nonEnum);
        Assert.Equal(typeof(string), otherType);
    }

    [Fact]
    public void ToJsonDocument_PrivateHandlesInputs()
    {
        var options = new JsonSerializerOptions();
        using var jsonDoc = JsonDocument.Parse("{\"root\":true}");
        using var docDoc = InvokePrivate<JsonDocument>("ToJsonDocument", jsonDoc, options);
        Assert.Equal(JsonValueKind.Object, docDoc.RootElement.ValueKind);

        var element = jsonDoc.RootElement.Clone();
        using var elementDoc = InvokePrivate<JsonDocument>("ToJsonDocument", element, options);
        Assert.True(elementDoc.RootElement.TryGetProperty("root", out _));

        using var nodeDoc = InvokePrivate<JsonDocument>("ToJsonDocument", JsonNode.Parse("{\"a\":1}")!, options);
        Assert.Equal("1", nodeDoc.RootElement.GetProperty("a").GetRawText());

        using var stringDoc = InvokePrivate<JsonDocument>("ToJsonDocument", "   ", options);
        Assert.Equal(JsonValueKind.Null, stringDoc.RootElement.ValueKind);

        using var emptyBytesDoc = InvokePrivate<JsonDocument>("ToJsonDocument", Array.Empty<byte>(), options);
        Assert.Equal(JsonValueKind.Null, emptyBytesDoc.RootElement.ValueKind);

        using var bytesDoc = InvokePrivate<JsonDocument>("ToJsonDocument", Encoding.UTF8.GetBytes("{\"b\":2}"), options);
        Assert.Equal("2", bytesDoc.RootElement.GetProperty("b").GetRawText());

        var segment = new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"c\":3}"));
        using var segmentDoc = InvokePrivate<JsonDocument>("ToJsonDocument", segment, options);
        Assert.Equal("3", segmentDoc.RootElement.GetProperty("c").GetRawText());

        var memory = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{\"d\":4}"));
        using var memoryDoc = InvokePrivate<JsonDocument>("ToJsonDocument", memory, options);
        Assert.Equal("4", memoryDoc.RootElement.GetProperty("d").GetRawText());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"e\":5}"));
        using var streamDoc = InvokePrivate<JsonDocument>("ToJsonDocument", stream, options);
        Assert.Equal("5", streamDoc.RootElement.GetProperty("e").GetRawText());

        using var emptyStream = new MemoryStream(Encoding.UTF8.GetBytes("   "));
        using var emptyStreamDoc = InvokePrivate<JsonDocument>("ToJsonDocument", emptyStream, options);
        Assert.Equal(JsonValueKind.Null, emptyStreamDoc.RootElement.ValueKind);

        using var charsDoc = InvokePrivate<JsonDocument>("ToJsonDocument", "{\"f\":6}".ToCharArray(), options);
        Assert.Equal("6", charsDoc.RootElement.GetProperty("f").GetRawText());

        using var defaultDoc = InvokePrivate<JsonDocument>("ToJsonDocument", new { g = 7 }, options);
        Assert.Equal("7", defaultDoc.RootElement.GetProperty("g").GetRawText());
    }

    [Fact]
    public void ExtractJsonString_PrivateHandlesInputs()
    {
        var options = new JsonSerializerOptions();
        using var document = JsonDocument.Parse("{\"a\":1}");
        var element = document.RootElement.Clone();
        var node = JsonNode.Parse("{\"b\":2}")!;

        Assert.Equal("{\"a\":1}", InvokePrivate<string>("ExtractJsonString", document, options));
        Assert.Equal("{\"a\":1}", InvokePrivate<string>("ExtractJsonString", element, options));
        Assert.Equal("{\"b\":2}", InvokePrivate<string>("ExtractJsonString", node, options));

        Assert.Equal(string.Empty, InvokePrivate<string>("ExtractJsonString", Array.Empty<byte>(), options));
        var segment = new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"c\":3}"));
        Assert.Equal("{\"c\":3}", InvokePrivate<string>("ExtractJsonString", segment, options));

        var memory = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{\"d\":4}"));
        Assert.Equal("{\"d\":4}", InvokePrivate<string>("ExtractJsonString", memory, options));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"e\":5}"));
        Assert.Equal("{\"e\":5}", InvokePrivate<string>("ExtractJsonString", stream, options));

        using var nonSeekable = new NonSeekableStream("{\"f\":6}");
        Assert.Equal("{\"f\":6}", InvokePrivate<string>("ExtractJsonString", nonSeekable, options));

        Assert.Equal("{\"g\":7}", InvokePrivate<string>("ExtractJsonString", "{\"g\":7}".ToCharArray(), options));
        Assert.Contains("\"h\":8", InvokePrivate<string>("ExtractJsonString", new { h = 8 }, options));

        Assert.Equal(string.Empty, TypeCoercionHelper.GetJsonText(Array.Empty<byte>(), options));
        Assert.Equal("{\"i\":9}", TypeCoercionHelper.GetJsonText(Encoding.UTF8.GetBytes("{\"i\":9}"), options));
    }

    [Fact]
    public void CoerceJsonValue_UsesDefaultOptionsAndWhitespaceJsonNodeReturnsNull()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = typeof(SampleHolder).GetProperty(nameof(SampleHolder.Node))!,
            IsJsonType = true,
            JsonSerializerOptions = null!
        };

        var result = TypeCoercionHelper.Coerce("   ", typeof(string), column);
        Assert.Null(result);
    }
}
