using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class TypeCoercionHelperTests
{
    private enum SampleEnum
    {
        None = 0,
        Value = 1
    }

    private sealed class TestColumnInfo : IColumnInfo
    {
        public string Name { get; init; } = "Test";
        public PropertyInfo PropertyInfo { get; init; } = typeof(TestColumnInfo).GetProperty(nameof(Name))!;
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

    private static PropertyInfo GetProperty<T>(string name) => typeof(T).GetProperty(name)!;

    private class EnumHolder
    {
        public SampleEnum EnumValue { get; set; }
        public JsonElement JsonElement { get; set; }
        public JsonNode? JsonNode { get; set; }
        public string? JsonText { get; set; }
    }

    [Fact]
    public void Coerce_WithEnumString_ParsesValue()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = GetProperty<EnumHolder>(nameof(EnumHolder.EnumValue)),
            IsEnum = true,
            EnumType = typeof(SampleEnum)
        };

        var result = TypeCoercionHelper.Coerce("Value", typeof(string), column, EnumParseFailureMode.Throw);

        Assert.Equal(SampleEnum.Value, result);
    }

    [Fact]
    public void Coerce_WithEnumNumber_ReturnsDefaultOnFailure()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = GetProperty<EnumHolder>(nameof(EnumHolder.EnumValue)),
            IsEnum = true,
            EnumType = typeof(SampleEnum)
        };

        var result = TypeCoercionHelper.Coerce(99, typeof(int), column, EnumParseFailureMode.SetNullAndLog);

        Assert.Null(result);
    }

    [Fact]
    public void Coerce_WithGuidSources_SupportsVariousRepresentations()
    {
        var options = TypeCoercionOptions.Default;
        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        Assert.Equal(guid, TypeCoercionHelper.Coerce(guid.ToString(), typeof(string), typeof(Guid), options));
        Assert.Equal(guid, TypeCoercionHelper.Coerce(guid.ToByteArray(), typeof(byte[]), typeof(Guid), options));
        Assert.Equal(guid, TypeCoercionHelper.Coerce(new ReadOnlyMemory<byte>(guid.ToByteArray()), typeof(ReadOnlyMemory<byte>), typeof(Guid), options));
        Assert.Equal(guid, TypeCoercionHelper.Coerce(new ArraySegment<byte>(guid.ToByteArray()), typeof(ArraySegment<byte>), typeof(Guid), options));
    }

    [Fact]
    public void CoerceBoolean_SupportsMultipleRepresentations()
    {
        var options = TypeCoercionOptions.Default;
        Assert.True((bool)TypeCoercionHelper.Coerce("true", typeof(string), typeof(bool), options)!);
        Assert.True((bool)TypeCoercionHelper.Coerce("1", typeof(string), typeof(bool), options)!);
        Assert.True((bool)TypeCoercionHelper.Coerce('Y', typeof(char), typeof(bool), options)!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0, typeof(int), typeof(bool), options)!);
        Assert.False((bool)TypeCoercionHelper.Coerce(0.0, typeof(double), typeof(bool), options)!);
    }

    [Fact]
    public void CoerceDateTimeOffset_UsesPolicy()
    {
        var options = new TypeCoercionOptions(TimeMappingPolicy.ForceUtcDateTime, JsonPassThrough.PreferDocument, SupportedDatabase.SqlServer);
        var dt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var dto = (DateTimeOffset)TypeCoercionHelper.Coerce(dt, typeof(DateTime), typeof(DateTimeOffset), options)!;
        Assert.Equal(DateTimeKind.Utc, dto.UtcDateTime.Kind);

        var options2 = new TypeCoercionOptions(TimeMappingPolicy.PreferDateTimeOffset, JsonPassThrough.PreferDocument, SupportedDatabase.SqlServer);
        var dto2 = (DateTimeOffset)TypeCoercionHelper.Coerce(dt, typeof(DateTime), typeof(DateTimeOffset), options2)!;
        Assert.Equal(TimeSpan.Zero, dto2.Offset);
    }

    [Fact]
    public void CoerceDateTime_ReturnsUtc()
    {
        var dto = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var result = (DateTime)TypeCoercionHelper.Coerce(dto, typeof(DateTimeOffset), typeof(DateTime))!;

        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void CoerceJsonValue_ToJsonElement()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = GetProperty<EnumHolder>(nameof(EnumHolder.JsonElement)),
            IsJsonType = true
        };

        var json = "{\"id\":1}";
        var value = TypeCoercionHelper.Coerce(json, typeof(string), column);

        Assert.IsType<JsonElement>(value);
        Assert.Equal(1, ((JsonElement)value!).GetProperty("id").GetInt32());
    }

    [Fact]
    public void CoerceJsonValue_ToJsonNode()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = GetProperty<EnumHolder>(nameof(EnumHolder.JsonNode)),
            IsJsonType = true
        };

        var json = JsonNode.Parse("{\"name\":\"test\"}");
        var value = TypeCoercionHelper.Coerce(json!, json!.GetType(), column);

        Assert.IsAssignableFrom<JsonNode>(value);
    }

    [Fact]
    public void CoerceJsonValue_DeserializesCustomType()
    {
        var column = new TestColumnInfo
        {
            PropertyInfo = GetProperty<EnumHolder>(nameof(EnumHolder.JsonText)),
            IsJsonType = true
        };

        var json = "\"hello\"";
        var value = TypeCoercionHelper.Coerce(json, typeof(string), column);

        Assert.Equal("\"hello\"", value);
    }

    [Fact]
    public void GetJsonText_ReadsFromStream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));
        var text = TypeCoercionHelper.GetJsonText(stream);
        Assert.Equal("{\"a\":1}", text);
    }
}
