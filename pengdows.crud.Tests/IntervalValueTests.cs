using System;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class IntervalValueTests
{
    [Fact]
    public void IntervalDaySecond_ComposesFromTimeSpan()
    {
        var span = TimeSpan.FromDays(3) + TimeSpan.FromHours(5);

        var value = IntervalDaySecond.FromTimeSpan(span);

        Assert.Equal(3, value.Days);
        Assert.Equal(TimeSpan.FromHours(5), value.Time);
        Assert.Equal(span, value.TotalTime);
        Assert.Equal("3 days 05:00:00", value.ToString());
    }

    [Fact]
    public void IntervalDaySecond_EqualityAndHash()
    {
        var left = new IntervalDaySecond(1, TimeSpan.FromMinutes(30));
        var right = new IntervalDaySecond(1, TimeSpan.FromMinutes(30));
        var different = new IntervalDaySecond(2, TimeSpan.Zero);

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left.Equals(different));
    }

    [Fact]
    public void IntervalYearMonth_ComposesFromTotalMonths()
    {
        var value = IntervalYearMonth.FromTotalMonths(27);

        Assert.Equal(2, value.Years);
        Assert.Equal(3, value.Months);
        Assert.Equal(27, value.TotalMonths);
        Assert.Equal("2 years 3 months", value.ToString());
    }

    [Fact]
    public void IntervalYearMonth_EqualityAndHash()
    {
        var left = new IntervalYearMonth(1, 2);
        var right = new IntervalYearMonth(1, 2);
        var different = new IntervalYearMonth(0, 14);

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left.Equals(different));
    }
}
