using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A database with no interval type takes a day-time interval as its ISO 8601 text, which reads back exactly (days and
/// fraction kept). A TimeSpan would be a time of day on Access or SQL Server: the day count is lost or refused.
/// </summary>
public class IntervalDaySecondNoTypeTests
{
    [Theory]
    [InlineData(SupportedDatabase.Access)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    public void WithNoIntervalTypeTheIntervalIsItsIsoText(SupportedDatabase provider)
    {
        var interval = IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)));

        var bound = new IntervalDaySecondConverter().ToProviderValue(interval, provider);

        var text = Assert.IsType<string>(bound);
        Assert.True(new IntervalDaySecondConverter().TryConvertFromProvider(text, provider, out var back));
        Assert.Equal(interval, back);
    }
}

/// <summary>
/// DuckDB has a native INTERVAL, which takes the day-time value as a TimeSpan (its days kept). It is named by its format,
/// not left to the no-interval-type default (ISO text), which DuckDB's INTERVAL column refuses.
/// </summary>
public class IntervalDaySecondDuckDbTests
{
    [Fact]
    public void OnDuckDbTheIntervalIsTheTimeSpanWithItsDays()
    {
        var interval = IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)));

        var bound = new IntervalDaySecondConverter().ToProviderValue(interval, SupportedDatabase.DuckDB);

        Assert.Equal(interval.TotalTime, Assert.IsType<TimeSpan>(bound));
    }
}
