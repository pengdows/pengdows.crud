using System;
using FirebirdSql.Data.Types;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-065: a Firebird TIME WITH TIME ZONE value is read as its UTC time on 0001-01-01, then
/// shown at its offset; a negative offset moved it before DateTime.MinValue and threw.
/// </summary>
public sealed class FirebirdZonedTimeReadTests
{
    // FirebirdClient builds FbZonedTime with an offset through an internal constructor.
    private static FbZonedTime Zoned(TimeSpan utc, TimeSpan offset) =>
        (FbZonedTime)typeof(FbZonedTime).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            new[] { typeof(TimeSpan), typeof(string), typeof(TimeSpan?) }, null)!
            .Invoke(new object?[] { utc, "UTC", (TimeSpan?)offset });

    [Theory]
    [InlineData(-5)]
    [InlineData(-14)]
    [InlineData(0)]
    [InlineData(3)]
    public void ZonedTime_AnyOffset_ReadsAsTheSameInstantAtItsOffset(int offsetHours)
    {
        var utc = new TimeSpan(2, 30, 0);
        var offset = TimeSpan.FromHours(offsetHours);
        var value = Zoned(utc, offset);

        Assert.True(FirebirdZonedDateTimeInterop.TryGetTime(value, out var time));

        Assert.Equal(offset, time.Offset);
        Assert.Equal(utc, time.UtcDateTime.TimeOfDay);
    }

    [Fact]
    public void ZonedTime_NonNegativeOffset_KeepsItsDocumentedDate()
    {
        Assert.True(FirebirdZonedDateTimeInterop.TryGetTime(Zoned(new TimeSpan(2, 30, 0), TimeSpan.Zero), out var time));

        Assert.Equal(DateTime.MinValue.Date, time.UtcDateTime.Date);
    }
}
