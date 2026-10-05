using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("AllocationSerial")]
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

    // Every decimal parameter calls Infer (SqlDialect.CreateDbParameter, SqlContainer.SetParameterValue).
    // It allocated an int[4] per call (decimal.GetBits) and divided once per integer digit: 428 ns
    // for 1234567890123.4567m against 40 ns for an int parameter.
    [Fact]
    public void Infer_AllocatesNothing()
    {
        decimal[] values = { 19.99m, 1234567890123.4567m, 12345678901234567.00m, -0.0001m, decimal.MaxValue };
        foreach (var v in values)
        {
            DecimalHelpers.Infer(v);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            DecimalHelpers.Infer(values[i % values.Length]);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // The algorithm Infer used before it was rewritten without decimal division, as the oracle.
    private static (int Precision, int Scale) ReferenceInfer(decimal value)
    {
        if (value == 0m)
        {
            return (0, 0);
        }

        var abs = Math.Abs(value);
        var scale = (decimal.GetBits(abs)[3] >> 16) & 0x7F;
        var mantissa = scale == 0 ? abs : abs * (decimal)Math.Pow(10, scale);
        while (scale > 0 && mantissa % 10m == 0m)
        {
            mantissa /= 10m;
            scale--;
        }

        var integerDigits = 0;
        var intPart = decimal.Truncate(abs);
        while (intPart >= 1m)
        {
            intPart /= 10m;
            integerDigits++;
        }

        return (integerDigits + scale, scale);
    }

    [Fact]
    public void Infer_MatchesTheReferenceAlgorithm_OnBoundaryAndRandomValues()
    {
        var values = new System.Collections.Generic.List<decimal>
        {
            1m, -1m, 0.1m, 0.10m, 1.0m, 100m, 100.00m, 0.0000000000000000000000000001m,
            1.0000000000000000000000000000m, 7.9228162514264337593543950335m, decimal.MaxValue, decimal.MinValue,
            10000000000000000000000000000m, 9999999999999999999999999999m, 12345678901234567.00m,
            0.5m, 0.05m, 0.050m, 99.99m, 1e-5m, 123456789.123456789m
        };
        var random = new Random(20261002);
        for (var i = 0; i < 5000; i++)
        {
            var scale = (byte)random.Next(0, 29);
            var candidate = new decimal(random.Next(), random.Next(), random.Next(0, 1 << 30), random.Next(2) == 0, scale);
            values.Add(candidate);
        }

        foreach (var v in values)
        {
            Assert.Equal(ReferenceInfer(v), DecimalHelpers.Infer(v));
        }
    }

    [Fact]
    public void CreateDbParameter_DecimalWithTrailingZeros_AllocatesNoMoreThanWithout()
    {
        var dialect = new DatabaseContext("Data Source=x;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer)).Dialect;
        long Allocated(decimal v)
        {
            dialect.CreateDbParameter("p", DbType.Decimal, v);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                dialect.CreateDbParameter("p", DbType.Decimal, v);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(Allocated(12345678901234567m), Allocated(12345678901234567.00m));
    }
}
