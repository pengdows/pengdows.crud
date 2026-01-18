using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.converters;

public static class IntervalConverterTests
{
    private static readonly IntervalYearMonthConverter YearMonthConverter = new();
    private static readonly IntervalDaySecondConverter DaySecondConverter = new();
    private static readonly PostgreSqlIntervalConverter PgIntervalConverter = new();

    [Fact]
    public static void IntervalYearMonthConverter_FormatsIso()
    {
        var interval = new IntervalYearMonth(2, 6);
        var provider = YearMonthConverter.ToProviderValue(interval, SupportedDatabase.PostgreSql);

        Assert.Equal("P2Y6M", provider);
        Assert.True(YearMonthConverter.TryConvertFromProvider("P2Y6M", SupportedDatabase.PostgreSql, out var parsed));
        Assert.Equal(interval, parsed);
    }

    [Fact]
    public static void IntervalDaySecondConverter_OutputDependsOnProvider()
    {
        var interval = new IntervalDaySecond(1, new TimeSpan(2, 3, 4));

        var iso = DaySecondConverter.ToProviderValue(interval, SupportedDatabase.PostgreSql);
        Assert.Equal("P1DT2H3M4S", iso);

        var timespan = DaySecondConverter.ToProviderValue(interval, SupportedDatabase.SqlServer);
        Assert.Equal(interval.TotalTime, timespan);
    }

    [Fact]
    public static void IntervalDaySecondConverter_ParsesIso()
    {
        Assert.True(DaySecondConverter.TryConvertFromProvider("P5DT1H", SupportedDatabase.PostgreSql, out var parsed));
        Assert.Equal(5, parsed.Days);
        Assert.Equal(TimeSpan.FromHours(1), parsed.Time);
    }

    [Fact]
    public static void PostgreSqlIntervalConverter_FormatsAndParses()
    {
        var interval = new PostgreSqlInterval(months: 3, days: 2, microseconds: 9_000_000);

        var iso = PgIntervalConverter.ToProviderValue(interval, SupportedDatabase.PostgreSql);
        Assert.Equal("P3M2DT9S", iso);

        Assert.True(PgIntervalConverter.TryConvertFromProvider(iso!, SupportedDatabase.PostgreSql, out var parsed));
        Assert.Equal(interval.Months, parsed.Months);
        Assert.Equal(interval.Days, parsed.Days);
        Assert.Equal(interval.Microseconds, parsed.Microseconds);
    }

    [Fact]
    public static void PostgreSqlIntervalConverter_HandlesNpgsqlShim()
    {
        var shim = new FakeNpgsqlTimeSpan
        {
            Months = 1,
            Days = 2,
            Ticks = TimeSpan.FromSeconds(3).Ticks
        };

        Assert.True(PgIntervalConverter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out var interval));
        Assert.Equal(1, interval.Months);
        Assert.Equal(2, interval.Days);
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks / 10, interval.Microseconds);
    }

    private sealed class FakeNpgsqlTimeSpan
    {
        public int Months { get; init; }
        public int Days { get; init; }
        public long Ticks { get; init; }
    }
}
