using System;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Found by the stage-0 type-system pins: text holding only a time of day ("13:45:30.5") read into a
/// DateTime, DateTimeOffset or DateOnly took today's date (DateTimeOffset.TryParse fills it in), a
/// value that depends on the day the read runs, with no error. Timestamp and date targets now need a
/// date in the text; a time of day still reads into TimeOnly and TimeSpan.
/// </summary>
public class TimeOnlyTextTests
{
    [Theory]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(DateOnly))]
    public void TimeOfDayText_IntoADateTarget_Fails(Type target)
    {
        Assert.Throws<InvalidCastException>(() => TypeCoercionHelper.Coerce("13:45:30.5", typeof(string), target));
    }

    [Fact]
    public void TimeOfDayText_IntoTimeTargets_StillReads()
    {
        Assert.Equal(new TimeOnly(13, 45, 30, 500), TypeCoercionHelper.Coerce("13:45:30.5", typeof(string), typeof(TimeOnly)));
        Assert.Equal(new TimeSpan(0, 13, 45, 30, 500), TypeCoercionHelper.Coerce("13:45:30.5", typeof(string), typeof(TimeSpan)));
    }

    // A date that is 0001-01-01 is still a date.
    [Fact]
    public void TheFirstDay_StillReads()
    {
        Assert.Equal(new DateTime(1, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            TypeCoercionHelper.Coerce("0001-01-01 10:00:00", typeof(string), typeof(DateTime)));
        Assert.Equal(new DateOnly(2026, 10, 5), TypeCoercionHelper.Coerce("2026-10-05 13:45:30", typeof(string), typeof(DateOnly)));
    }
}
