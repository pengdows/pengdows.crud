using System;
using FirebirdSql.Data.Types;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A Firebird TIME WITH TIME ZONE keeps its offset: it is written as the UTC time in the Etc/GMT zone of the offset (a
/// whole-hour offset; any other offset is refused, never sent as UTC), and read back at that zone's offset.
/// </summary>
public sealed class FirebirdZonedTimeOffsetTests
{
    [Fact]
    public void AWholeHourOffsetIsSentAsTheUtcTimeInItsEtcZone()
    {
        var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2));

        var zoned = (FbZonedTime)FirebirdZonedDateTimeInterop.CreateUtcTime(value, null)!;

        Assert.Equal("Etc/GMT-2", zoned.TimeZone);
        Assert.Equal(new TimeSpan(11, 45, 30), zoned.Time);
    }

    [Fact]
    public void AZeroOffsetIsSentInTheUtcZone()
    {
        var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.Zero);

        var zoned = (FbZonedTime)FirebirdZonedDateTimeInterop.CreateUtcTime(value, null)!;

        Assert.Equal("UTC", zoned.TimeZone);
    }

    [Fact]
    public void AnOffsetThatIsNotWholeHoursIsRefusedNotSentAsUtc()
    {
        var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, new TimeSpan(5, 30, 0));

        Assert.Throws<NotSupportedException>(() => FirebirdZonedDateTimeInterop.CreateUtcTime(value, null));
    }

    [Fact]
    public void AZoneNameReadsAsItsOffset()
    {
        var zoned = new FbZonedTime(new TimeSpan(11, 45, 30), "Etc/GMT-2");

        Assert.True(FirebirdZonedDateTimeInterop.TryGetTime(zoned, out var time));

        Assert.Equal(TimeSpan.FromHours(2), time.Offset);
        Assert.Equal(new TimeSpan(11, 45, 30), time.UtcDateTime.TimeOfDay);
    }

    [Fact]
    public void A_NegativeZoneNameReadsAsANegativeOffset()
    {
        var zoned = new FbZonedTime(new TimeSpan(11, 45, 30), "Etc/GMT+5");

        Assert.True(FirebirdZonedDateTimeInterop.TryGetTime(zoned, out var time));

        Assert.Equal(TimeSpan.FromHours(-5), time.Offset);
    }

    [Fact]
    public void WriteAndReadRoundTripTheOffset()
    {
        var value = new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2));
        var zoned = FirebirdZonedDateTimeInterop.CreateUtcTime(value, null);

        Assert.True(FirebirdZonedDateTimeInterop.TryGetTime(zoned!, out var read));

        Assert.Equal(value.Offset, read.Offset);
        Assert.Equal(value.TimeOfDay, read.TimeOfDay);
    }
}
