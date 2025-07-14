#region

using System;
using System.Globalization;
using System.Text.Json;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TypeCoercionHelperTests
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Coerce_StringToInt_ReturnsInt()
    {
        var typeRegistry = new TypeMapRegistry();
        var ti = typeRegistry.GetTableInfo<SampleEntity>();
        ti.Columns.TryGetValue("MaxValue", out var maxValue);
        var result = TypeCoercionHelper.Coerce("123", typeof(string), maxValue);
        Assert.Equal(123, result);
    }


    [Fact]
    public void Coerce_NullValue_ReturnsNull()
    {
        var result = TypeCoercionHelper.Coerce(null, typeof(string), typeof(string));
        Assert.Null(result);
    }

    [Fact]
    public void Coerce_StringToDateTime_ParsesCorrectly()
    {
        var input = "2023-04-30T10:00:00Z";
        var expected = DateTime.Parse(input, null,
            DateTimeStyles.AdjustToUniversal |
            DateTimeStyles.AssumeUniversal);
        var result = TypeCoercionHelper.Coerce(input, typeof(string), typeof(DateTime));

        Assert.Equal(expected, result);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)result!).Kind); // Optional extra assertion
    }

    [Fact]
    public void Coerce_StringToDateTime_Invalid_Throws()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("not-a-date", typeof(string), typeof(DateTime)));
    }

    [Fact]
    public void Coerce_ValidEnumString_ReturnsEnum()
    {
        var column = new ColumnInfo
            { EnumType = typeof(TestEnum), PropertyInfo = typeof(TestEnum).GetProperty("HasFlag") };
        var result = TypeCoercionHelper.Coerce("Two", typeof(string), column);
        Assert.Equal(TestEnum.Two, result);
    }

    [Fact]
    public void Coerce_InvalidEnumString_ThrowMode_Throws()
    {
        var column = new ColumnInfo
            { EnumType = typeof(TestEnum), PropertyInfo = typeof(TestEnum).GetProperty("HasFlag") };
        Assert.Throws<ArgumentException>(() =>
            TypeCoercionHelper.Coerce("Invalid", typeof(string), column, EnumParseFailureMode.Throw));
    }

    [Fact]
    public void Coerce_InvalidEnumString_SetNullAndLog_ReturnsNull()
    {
        var column = new ColumnInfo
        {
            EnumType = typeof(TestEnum),
            PropertyInfo = typeof(TestEnum?).GetProperty("HasValue")
        };
        var result = TypeCoercionHelper.Coerce("Invalid", typeof(string), column, EnumParseFailureMode.SetNullAndLog);
        Assert.Null(result);
    }

    [Fact]
    public void Coerce_ValidGuidString_ReturnsGuid()
    {
        var guid = Guid.NewGuid();
        var result = TypeCoercionHelper.Coerce(guid.ToString(), typeof(string), typeof(Guid));
        Assert.Equal(guid, result);
    }

    [Fact]
    public void Coerce_InvalidGuidString_Throws()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("invalid-guid", typeof(string), typeof(Guid)));
    }

    [Fact]
    public void Coerce_JsonToObject_ParsesCorrectly()
    {
        var json = "{\"Name\":\"Test\"}";
        var column = new ColumnInfo
        {
            PropertyInfo = typeof(CustomEntity).GetProperty("customObject"),
            IsJsonType = true,
            JsonSerializerOptions = _jsonOptions
        };
        var result = TypeCoercionHelper.Coerce(json, typeof(ColumnInfo), column) as CustomObject;
        Assert.NotNull(result);
        Assert.Equal("Test", result?.Name);
    }

    [Fact]
    public void Coerce_StringToInt_ParsesCorrectly()
    {
        var result = TypeCoercionHelper.Coerce("42", typeof(string), typeof(int));
        Assert.Equal(42, result);
    }

    [Fact]
    public void Coerce_StringToBool_ParsesCorrectly()
    {
        var result = TypeCoercionHelper.Coerce("true", typeof(string), typeof(bool));
        Assert.True((bool)result!);
    }

    [Fact]
    public void Coerce_InvalidPrimitiveConversion_Throws()
    {
        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.Coerce("hello", typeof(string), typeof(int)));
    }

    private enum TestEnum
    {
        One,
        Two,
        Three
    }

    private class CustomEntity
    {
        public CustomObject customObject { get; set; }
    }

    private class CustomObject
    {
        public string Name { get; set; } = string.Empty;
    }
    [Fact]
    public void Coerce_InvalidEnumString_SetDefaultValue_ReturnsDefault()
    {
        var column = new ColumnInfo
        {
            EnumType = typeof(TestEnum),
            PropertyInfo = typeof(EnumHolder).GetProperty("Value")
        };
        var result = TypeCoercionHelper.Coerce("Invalid", typeof(string), column, EnumParseFailureMode.SetDefaultValue);
        Assert.Equal(TestEnum.One, result);
    }

    private class EnumHolder { public TestEnum Value { get; set; } }
}
