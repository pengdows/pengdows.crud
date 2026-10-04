using System;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class ValueObjectCoverageTests
{

    [Fact]
    public void PostgreSqlInterval_FromTimeSpan_PreservesPrecision()
    {
        // Ported from the 2.0 branch. Production behavior differs from 2.0: FromTimeSpan splits
        // whole days into Days and only the sub-day remainder into Microseconds (rather than
        // putting the span's total ticks into Microseconds while also setting Days) — see
        // PostgreSqlInterval.FromTimeSpan(). Updated to assert the current split.
        var span = TimeSpan.FromDays(3) + TimeSpan.FromMinutes(5);
        var converted = PostgreSqlInterval.FromTimeSpan(span);
        Assert.Equal(3, converted.Days);
        // Microseconds holds only the sub-day part; whole days live in Days.
        Assert.Equal(TimeSpan.FromMinutes(5).Ticks / 10, converted.Microseconds);
        Assert.Equal(0, converted.Months);
    }

    [Theory]
    [InlineData(3, 0, 5, 0)]
    [InlineData(0, 23, 59, 59)]
    [InlineData(-1, 0, -5, 0)]
    [InlineData(400, 12, 0, 1)]
    public void PostgreSqlInterval_FromTimeSpan_RoundTrips(int days, int hours, int minutes, int seconds)
    {
        var span = new TimeSpan(days, hours, minutes, seconds);

        Assert.Equal(span, PostgreSqlInterval.FromTimeSpan(span).ToTimeSpan());
    }

}
