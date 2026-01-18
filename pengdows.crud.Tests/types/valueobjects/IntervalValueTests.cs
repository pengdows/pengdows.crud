using System;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.valueobjects;

public static class IntervalValueTests
{
    [Fact]
    public static void IntervalDaySecond_TotalTimeCombinesComponents()
    {
        var interval = new IntervalDaySecond(2, TimeSpan.FromHours(3));
        Assert.Equal(TimeSpan.FromDays(2) + TimeSpan.FromHours(3), interval.TotalTime);
        Assert.Equal("2 days 03:00:00", interval.ToString());
    }

    [Fact]
    public static void IntervalDaySecond_FromTimeSpan_ExtractsDaysAndResidual()
    {
        var value = IntervalDaySecond.FromTimeSpan(TimeSpan.FromHours(50));
        Assert.Equal(2, value.Days);
        Assert.Equal(TimeSpan.FromHours(2), value.Time);
    }

    [Fact]
    public static void IntervalYearMonth_TotalMonthsChecksOverflow()
    {
        var interval = new IntervalYearMonth(1, 6);
        Assert.Equal(18, interval.TotalMonths);
        Assert.Equal("1 years 6 months", interval.ToString());
    }

    [Fact]
    public static void IntervalYearMonth_FromTotalMonths_Decomposes()
    {
        var value = IntervalYearMonth.FromTotalMonths(25);
        Assert.Equal(2, value.Years);
        Assert.Equal(1, value.Months);
    }

    [Fact]
    public static void PostgreSqlInterval_ToTimeSpanCombinesComponents()
    {
        var interval = new PostgreSqlInterval(months: 0, days: 1, microseconds: 1_500_000);
        Assert.Equal(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1.5), interval.ToTimeSpan());
        Assert.Equal("0 months 1 days 1500000 microseconds", interval.ToString());
    }

    [Fact]
    public static void PostgreSqlInterval_FromTimeSpan_ConvertsTicks()
    {
        var value = PostgreSqlInterval.FromTimeSpan(TimeSpan.FromMilliseconds(1234));
        Assert.Equal((long)(TimeSpan.FromMilliseconds(1234).Ticks / 10), value.Microseconds);
    }
}
