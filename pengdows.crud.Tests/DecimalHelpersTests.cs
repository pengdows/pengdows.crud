using pengdows.crud;
using Xunit;

namespace pengdows.crud.Tests;

public class DecimalHelpersTests
{
    [Fact]
    public void Infer_WithZero_ReturnsZeroPrecisionAndScale()
    {
        var result = DecimalHelpers.Infer(0m);
        
        Assert.Equal(0, result.Precision);
        Assert.Equal(0, result.Scale);
    }

    [Fact]
    public void Infer_WithWholeNumber_ReturnsCorrectPrecision()
    {
        var result = DecimalHelpers.Infer(123m);
        
        Assert.Equal(3, result.Precision);
        Assert.Equal(0, result.Scale);
    }

    [Fact]
    public void Infer_WithDecimalPlaces_ReturnsCorrectPrecisionAndScale()
    {
        var result = DecimalHelpers.Infer(123.45m);
        
        Assert.Equal(5, result.Precision);
        Assert.Equal(2, result.Scale);
    }

    [Fact]
    public void Infer_WithNegativeNumber_ReturnsCorrectPrecisionAndScale()
    {
        var result = DecimalHelpers.Infer(-123.45m);
        
        Assert.Equal(5, result.Precision);
        Assert.Equal(2, result.Scale);
    }

    [Fact]
    public void Infer_WithLeadingZeros_ReturnsCorrectPrecision()
    {
        var result = DecimalHelpers.Infer(0.123m);
        
        Assert.Equal(3, result.Precision);
        Assert.Equal(3, result.Scale);
    }

    [Fact]
    public void Infer_WithMaxDecimal_HandlesLargeValues()
    {
        var result = DecimalHelpers.Infer(decimal.MaxValue);
        
        Assert.True(result.Precision > 0);
        Assert.Equal(0, result.Scale);
    }

    [Fact]
    public void Infer_WithMinDecimal_HandlesLargeNegativeValues()
    {
        var result = DecimalHelpers.Infer(decimal.MinValue);
        
        Assert.True(result.Precision > 0);
        Assert.Equal(0, result.Scale);
    }

    [Fact]
    public void Infer_WithSmallDecimal_HandlesSmallValues()
    {
        var result = DecimalHelpers.Infer(0.000001m);
        
        Assert.Equal(6, result.Precision);
        Assert.Equal(6, result.Scale);
    }

    [Fact]
    public void Infer_WithSingleDigitDecimal_ReturnsCorrectValues()
    {
        var result = DecimalHelpers.Infer(1.5m);
        
        Assert.Equal(2, result.Precision);
        Assert.Equal(1, result.Scale);
    }

    [Fact]
    public void Infer_WithTrailingDecimalZeros_HandlesCorrectly()
    {
        var result = DecimalHelpers.Infer(123.10m);
        
        Assert.Equal(4, result.Precision);
        Assert.Equal(1, result.Scale);
    }
}