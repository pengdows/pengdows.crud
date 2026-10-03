using System;
using Xunit;

namespace pengdows.crud.Tests;

[CollectionDefinition("TimeZoneSerial", DisableParallelization = true)]
public sealed class TimeZoneSerialCollection
{
}

/// <summary>
/// REV-059: timestamp text with no offset (e.g. SQLite TEXT "2024-01-02T03:04:05") was parsed with
/// DateTimeOffset.TryParse, which assumes the machine's local time: 09:04Z on a UTC-6 host, 03:04Z
/// on a UTC host. Every other path treats an unspecified time as UTC, so text does too. The host
/// time zone is pinned to America/Chicago (UTC-6 in January) so the test fails on any machine.
/// </summary>
[Collection("TimeZoneSerial")]
public sealed class OffsetlessTimestampTextTests : IDisposable
{
    private readonly string? _previousTz;

    public OffsetlessTimestampTextTests()
    {
        _previousTz = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", "America/Chicago");
        TimeZoneInfo.ClearCachedData();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TZ", _previousTz);
        TimeZoneInfo.ClearCachedData();
    }

    private static readonly DateTime Utc = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void HostIsPinnedOffUtc()
    {
        Assert.NotEqual(TimeSpan.Zero, TimeZoneInfo.Local.GetUtcOffset(Utc));
    }

    [Fact]
    public void OffsetlessText_ToDateTime_IsUtc()
    {
        var viaCoerce = (DateTime)TypeCoercionHelper.Coerce("2024-01-02T03:04:05", typeof(string), typeof(DateTime))!;
        var viaTyped = TypeCoercionHelper.CoerceDateTimeFromString("2024-01-02T03:04:05");

        Assert.Equal(Utc, viaCoerce);
        Assert.Equal(DateTimeKind.Utc, viaCoerce.Kind);
        Assert.Equal(Utc, viaTyped);
        Assert.Equal(DateTimeKind.Utc, viaTyped.Kind);
    }

    [Fact]
    public void OffsetlessText_ToDateTimeOffset_IsUtc()
    {
        var viaCoerce = (DateTimeOffset)TypeCoercionHelper.Coerce("2024-01-02T03:04:05", typeof(string), typeof(DateTimeOffset))!;
        var viaTyped = TypeCoercionHelper.CoerceDateTimeOffsetFromString("2024-01-02T03:04:05");

        Assert.Equal(new DateTimeOffset(Utc), viaCoerce);
        Assert.Equal(TimeSpan.Zero, viaCoerce.Offset);
        Assert.Equal(new DateTimeOffset(Utc), viaTyped);
        Assert.Equal(TimeSpan.Zero, viaTyped.Offset);
    }

    [Fact]
    public void TextWithAnOffset_KeepsItsInstant()
    {
        const string text = "2024-01-02T03:04:05-06:00";
        var instant = new DateTime(2024, 1, 2, 9, 4, 5, DateTimeKind.Utc);

        Assert.Equal(instant, (DateTime)TypeCoercionHelper.Coerce(text, typeof(string), typeof(DateTime))!);
        Assert.Equal(instant, TypeCoercionHelper.CoerceDateTimeFromString(text));
        var dto = TypeCoercionHelper.CoerceDateTimeOffsetFromString(text);
        Assert.Equal(TimeSpan.FromHours(-6), dto.Offset);
        Assert.Equal(instant, dto.UtcDateTime);
        Assert.Equal(instant, TypeCoercionHelper.CoerceDateTimeFromString("2024-01-02T09:04:05Z"));
    }
}
