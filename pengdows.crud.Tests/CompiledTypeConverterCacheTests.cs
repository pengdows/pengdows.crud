using System;
using System.Globalization;
using Xunit;

namespace pengdows.crud.Tests;

public class CompiledTypeConverterCacheTests
{
    [Fact]
    public void ConvertWithCache_Int32ToInt64_ConvertsCorrectly()
    {
        int source = 42;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(long));

        Assert.Equal(42L, result);
        Assert.IsType<long>(result);
    }

    [Fact]
    public void ConvertWithCache_Int64ToInt32_ConvertsCorrectly()
    {
        long source = 42L;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(int));

        Assert.Equal(42, result);
        Assert.IsType<int>(result);
    }

    [Fact]
    public void ConvertWithCache_StringToInt32_ConvertsCorrectly()
    {
        string source = "123";

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(int));

        Assert.Equal(123, result);
        Assert.IsType<int>(result);
    }

    [Fact]
    public void ConvertWithCache_DoubleToDecimal_ConvertsCorrectly()
    {
        double source = 123.45;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(decimal));

        Assert.Equal(123.45m, result);
        Assert.IsType<decimal>(result);
    }

    [Fact]
    public void ConvertWithCache_Int32ToNullableInt64_ConvertsCorrectly()
    {
        int source = 42;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(long?));

        Assert.Equal(42L, result);
        Assert.IsType<long>(result);
    }

    [Fact]
    public void ConvertWithCache_SameTypePairTwice_UsesCachedConverter()
    {
        int source1 = 10;
        int source2 = 20;

        var result1 = TypeCoercionHelper.ConvertWithCache(source1, typeof(long));
        var result2 = TypeCoercionHelper.ConvertWithCache(source2, typeof(long));

        Assert.Equal(10L, result1);
        Assert.Equal(20L, result2);
        // Cache hit means same converter used (we can't directly test this without exposing cache)
    }

    [Fact]
    public void ConvertWithCache_StringToGuid_ConvertsCorrectly()
    {
        string source = "12345678-1234-1234-1234-123456789abc";

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(Guid));

        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), result);
        Assert.IsType<Guid>(result);
    }

    [Fact]
    public void ConvertWithCache_Int32ToString_ConvertsCorrectly()
    {
        int source = 42;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(string));

        Assert.Equal("42", result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public void ConvertWithCache_BooleanToInt32_ConvertsCorrectly()
    {
        bool source = true;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(int));

        Assert.Equal(1, result);
        Assert.IsType<int>(result);
    }

    [Fact]
    public void ConvertWithCache_Int32ToBoolean_ConvertsCorrectly()
    {
        int source = 1;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(bool));

        Assert.Equal(true, result);
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void ConvertWithCache_InvalidConversion_ThrowsException()
    {
        string source = "not a number";

        Assert.Throws<InvalidCastException>(() =>
            TypeCoercionHelper.ConvertWithCache(source, typeof(int)));
    }

    [Fact]
    public void ConvertWithCache_NullableTarget_HandlesNull()
    {
        // This test verifies the converter handles nullable target types
        int source = 42;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(int?));

        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertWithCache_DateTimeToDateTimeOffset_ConvertsCorrectly()
    {
        var source = new DateTime(2026, 2, 7, 12, 0, 0, DateTimeKind.Utc);

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(DateTimeOffset));

        Assert.IsType<DateTimeOffset>(result);
        var dto = (DateTimeOffset)result;
        Assert.Equal(2026, dto.Year);
        Assert.Equal(2, dto.Month);
        Assert.Equal(7, dto.Day);
    }

    [Fact]
    public void ConvertWithCache_DecimalToDouble_ConvertsCorrectly()
    {
        decimal source = 123.45m;

        var result = TypeCoercionHelper.ConvertWithCache(source, typeof(double));

        Assert.Equal(123.45, (double)result, precision: 2);
        Assert.IsType<double>(result);
    }
}
