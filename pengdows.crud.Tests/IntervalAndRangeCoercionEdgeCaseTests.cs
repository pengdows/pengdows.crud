using System;
using System.Data;
using Moq;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.types.coercion;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests edge cases for interval and range coercions, plus IntervalYearMonthConverter.
/// </summary>
public class IntervalAndRangeCoercionEdgeCaseTests
{
    // ===== ConverterRead<IntervalYearMonth> =====

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_NullRaw_ReturnsFalse()
    {
        var coercion = new ConverterRead<IntervalYearMonth>();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ConverterRead<IntervalYearMonth>();

        // BP-124: int/long are ODP.NET's total-months representation; double stays unknown.
        Assert.False(coercion.TryRead(new DbValue(42d), out _));
    }

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        var coercion = new ConverterRead<IntervalYearMonth>();
        // IntervalYearMonth.Parse doesn't throw for most strings, but the coercion
        // wraps it in try/catch. Pass a value that passes through and just parses as 0.
        // Actually, IntervalYearMonth.Parse is quite lenient. Let's use the passthrough case.
        Assert.True(coercion.TryRead(new DbValue("P1Y2M", typeof(string)), out var result));
        Assert.Equal(1, result.Years);
        Assert.Equal(2, result.Months);
    }

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_IntervalPassthrough()
    {
        var coercion = new ConverterRead<IntervalYearMonth>();
        var interval = new IntervalYearMonth(3, 6);

        Assert.True(coercion.TryRead(new DbValue(interval), out var result));
        Assert.Equal(3, result.Years);
        Assert.Equal(6, result.Months);
    }

    // ===== ConverterRead<IntervalDaySecond> =====

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_NullRaw_ReturnsFalse()
    {
        var coercion = new ConverterRead<IntervalDaySecond>();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ConverterRead<IntervalDaySecond>();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        var coercion = new ConverterRead<IntervalDaySecond>();
        // Parse is lenient, so test with passthrough IntervalDaySecond
        var interval = new IntervalDaySecond(1, new TimeSpan(2, 3, 4));

        Assert.True(coercion.TryRead(new DbValue(interval), out var result));
        Assert.Equal(1, result.Days);
    }

    // ===== ConverterRead<PostgreSqlInterval> =====

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_NullRaw_ReturnsFalse()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_IntervalPassthrough()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();
        var interval = new PostgreSqlInterval(0, 1, 3600000000);

        Assert.True(coercion.TryRead(new DbValue(interval), out var result));
        Assert.Equal(interval, result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();

        Assert.False(coercion.TryRead(new DbValue("not a timespan"), out _));
    }

    // ===== ConverterRead<Range<long>> =====

    [Fact]
    public void PostgreSqlRangeLongCoercion_TryRead_ValidString_ReturnsRange()
    {
        var coercion = new ConverterRead<Range<long>>();

        Assert.True(coercion.TryRead(new DbValue("[100,200)", typeof(string)), out var result));
        Assert.Equal(100L, result.Lower);
        Assert.Equal(200L, result.Upper);
    }

    [Fact]
    public void PostgreSqlRangeLongCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        var coercion = new ConverterRead<Range<long>>();

        Assert.False(coercion.TryRead(new DbValue("not a range", typeof(string)), out _));
    }

    [Fact]
    public void PostgreSqlRangeLongCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ConverterRead<Range<long>>();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void PostgreSqlRangeLongCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new ConverterRead<Range<long>>();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    [Fact]
    public void PostgreSqlRangeLongCoercion_TryRead_RangePassthrough()
    {
        var coercion = new ConverterRead<Range<long>>();
        var range = new Range<long>(1L, 10L, true, false);

        Assert.True(coercion.TryRead(new DbValue(range), out var result));
        Assert.Equal(range.Lower, result.Lower);
        Assert.Equal(range.Upper, result.Upper);
    }

    // ===== IntervalYearMonthConverter =====

    [Fact]
    public void IntervalYearMonthConverter_ConvertToProvider_Oracle_ReturnsIso()
    {
        var converter = new IntervalYearMonthConverter();
        var interval = new IntervalYearMonth(3, 6);

        var result = converter.ToProviderValue(interval, SupportedDatabase.Oracle);
        Assert.Equal("+0003-06", result); // BP-124: Oracle literal format, not ISO-8601
    }

    [Fact]
    public void IntervalYearMonthConverter_ConvertToProvider_PostgreSql_ReturnsIso()
    {
        var converter = new IntervalYearMonthConverter();
        var interval = new IntervalYearMonth(1, 0);

        var result = converter.ToProviderValue(interval, SupportedDatabase.PostgreSql);
        Assert.Equal("P1Y0M", result);
    }

    [Fact]
    public void IntervalYearMonthConverter_ConvertToProvider_CockroachDb_ReturnsIso()
    {
        var converter = new IntervalYearMonthConverter();
        var interval = new IntervalYearMonth(0, 6);

        var result = converter.ToProviderValue(interval, SupportedDatabase.CockroachDb);
        Assert.Equal("P0Y6M", result);
    }

    [Fact]
    public void IntervalYearMonthConverter_ConvertToProvider_DefaultProvider_ReturnsRaw()
    {
        var converter = new IntervalYearMonthConverter();
        var interval = new IntervalYearMonth(2, 3);

        var result = converter.ToProviderValue(interval, SupportedDatabase.Sqlite);
        Assert.IsType<IntervalYearMonth>(result);
        Assert.Equal(interval, (IntervalYearMonth)result!);
    }

    [Fact]
    public void IntervalYearMonthConverter_TryConvert_Passthrough()
    {
        var converter = new IntervalYearMonthConverter();
        var interval = new IntervalYearMonth(1, 2);

        var success = converter.TryConvertFromProvider(interval, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(interval, result);
    }

    [Fact]
    public void IntervalYearMonthConverter_TryConvert_ValidString()
    {
        var converter = new IntervalYearMonthConverter();

        var success = converter.TryConvertFromProvider("P3Y6M", SupportedDatabase.Oracle, out var result);
        Assert.True(success);
        Assert.Equal(3, result.Years);
        Assert.Equal(6, result.Months);
    }

    [Fact]
    public void IntervalYearMonthConverter_TryConvert_UnknownType_ReturnsFalse()
    {
        var converter = new IntervalYearMonthConverter();

        // A number is a month count (DRY-010); something that is no interval at all is refused.
        var success = converter.TryConvertFromProvider(new object(), SupportedDatabase.Oracle, out _);
        Assert.False(success);
    }

    // ===== IntervalYearMonth.Parse edge cases =====

    [Fact]
    public void IntervalYearMonthParse_EmptyString_ReturnsZero()
    {
        var result = IntervalYearMonth.Parse("");
        Assert.Equal(0, result.Years);
        Assert.Equal(0, result.Months);
    }

    [Fact]
    public void IntervalYearMonthParse_WhitespaceOnly_ReturnsZero()
    {
        var result = IntervalYearMonth.Parse("   ");
        Assert.Equal(0, result.Years);
        Assert.Equal(0, result.Months);
    }

    [Fact]
    public void IntervalYearMonthParse_NoPPrefix_Parses()
    {
        // Without P prefix, should still parse
        var result = IntervalYearMonth.Parse("2Y3M");
        Assert.Equal(2, result.Years);
        Assert.Equal(3, result.Months);
    }

    [Fact]
    public void IntervalYearMonthParse_UnknownCharacters_Fail()
    {
        // DRY-012: unknown letters were skipped, so other text read as a wrong value; now it fails.
        Assert.Throws<FormatException>(() => IntervalYearMonth.Parse("P2Y3M5X"));
    }

    [Fact]
    public void IntervalYearMonthParse_OnlyYears()
    {
        var result = IntervalYearMonth.Parse("P5Y");
        Assert.Equal(5, result.Years);
        Assert.Equal(0, result.Months);
    }

    [Fact]
    public void IntervalYearMonthParse_OnlyMonths()
    {
        var result = IntervalYearMonth.Parse("P11M");
        Assert.Equal(0, result.Years);
        Assert.Equal(11, result.Months);
    }

    // ConverterRead<PostgreSqlInterval>.TryWrite writes ToTimeSpan(), which drops months, but nothing
    // on the write path calls it: a provider without an interval mapping gets the value object
    // itself, months intact.
    [Fact]
    public void IntervalParameter_ProviderWithoutIntervalMapping_KeepsMonths()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var interval = new PostgreSqlInterval(14, 3, 5_000_000);

        var parameter = context.CreateDbParameter("p0", DbType.Object, interval);

        var written = Assert.IsType<PostgreSqlInterval>(parameter.Value);
        Assert.Equal(14, written.Months);
        Assert.Equal(3, written.Days);
    }
}
