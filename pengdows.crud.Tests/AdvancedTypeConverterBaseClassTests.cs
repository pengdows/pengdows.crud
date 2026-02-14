using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests the AdvancedTypeConverter&lt;T&gt; base class default method paths
/// using a minimal passthrough converter that does NOT override any methods.
/// </summary>
public class AdvancedTypeConverterBaseClassTests
{
    /// <summary>
    /// Minimal converter that inherits all base class defaults.
    /// </summary>
    private class PassthroughConverter<T> : AdvancedTypeConverter<T>
    {
    }

    [Fact]
    public void ToProviderValue_NullInput_ReturnsNull()
    {
        var converter = new PassthroughConverter<string>();
        var result = converter.ToProviderValue(null!, SupportedDatabase.Sqlite);
        Assert.Null(result);
    }

    [Fact]
    public void ToProviderValue_WrongType_ThrowsArgumentException()
    {
        var converter = new PassthroughConverter<int>();
        var ex = Assert.Throws<ArgumentException>(() =>
            converter.ToProviderValue("not an int", SupportedDatabase.Sqlite));
        Assert.Contains("Int32", ex.Message);
        Assert.Contains("String", ex.Message);
    }

    [Fact]
    public void ToProviderValue_CorrectType_ReturnsValue()
    {
        var converter = new PassthroughConverter<int>();
        var result = converter.ToProviderValue(42, SupportedDatabase.Sqlite);
        Assert.Equal(42, result);
    }

    [Fact]
    public void FromProviderValue_NullInput_ReturnsNull()
    {
        var converter = new PassthroughConverter<string>();
        var result = converter.FromProviderValue(null!, SupportedDatabase.Sqlite);
        Assert.Null(result);
    }

    [Fact]
    public void FromProviderValue_DBNullInput_ReturnsNull()
    {
        var converter = new PassthroughConverter<string>();
        var result = converter.FromProviderValue(DBNull.Value, SupportedDatabase.Sqlite);
        Assert.Null(result);
    }

    [Fact]
    public void FromProviderValue_UnconvertibleType_ReturnsNull()
    {
        var converter = new PassthroughConverter<int>();
        var result = converter.FromProviderValue("not an int", SupportedDatabase.Sqlite);
        Assert.Null(result);
    }

    [Fact]
    public void DefaultConvertToProvider_ReturnsValueUnchanged()
    {
        var converter = new PassthroughConverter<string>();
        var result = converter.ToProviderValue("hello", SupportedDatabase.Sqlite);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void DefaultTryConvertFromProvider_MatchingType_ReturnsTrue()
    {
        var converter = new PassthroughConverter<string>();
        var success = converter.TryConvertFromProvider("test", SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal("test", result);
    }

    [Fact]
    public void DefaultTryConvertFromProvider_NonMatchingType_ReturnsFalse()
    {
        var converter = new PassthroughConverter<int>();
        var success = converter.TryConvertFromProvider("not int", SupportedDatabase.Sqlite, out var result);
        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TargetType_ReturnsCorrectType()
    {
        var converter = new PassthroughConverter<Guid>();
        Assert.Equal(typeof(Guid), converter.TargetType);
    }

    [Fact]
    public void FromProviderValue_MatchingType_ReturnsValue()
    {
        var converter = new PassthroughConverter<int>();
        var result = converter.FromProviderValue(42, SupportedDatabase.Sqlite);
        Assert.Equal(42, result);
    }
}
