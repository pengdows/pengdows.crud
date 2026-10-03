using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-060: PostgreSQL's verbose interval text puts one sign on the whole clock ("-01:30:00" is
/// minus an hour and a half). The sign was read with the hours only, so "-01:30:00" became -30 min
/// and "-00:30:00" became +30 min.
/// </summary>
public sealed class PostgreSqlIntervalNegativeClockTests
{
    private static PostgreSqlInterval Read(string text)
    {
        var converter = new PostgreSqlIntervalConverter();
        Assert.True(converter.TryConvertFromProvider(text, SupportedDatabase.PostgreSql, out var value));
        return value;
    }

    [Theory]
    [InlineData("-01:30:00", -5_400_000_000L)]
    [InlineData("-00:30:00", -1_800_000_000L)]
    [InlineData("-00:00:00.5", -500_000L)]
    [InlineData("01:30:00", 5_400_000_000L)]
    [InlineData("+01:30:00", 5_400_000_000L)]
    public void ClockText_SignAppliesToTheWholeClock(string text, long microseconds)
    {
        Assert.Equal(microseconds, Read(text).Microseconds);
    }

    [Fact]
    public void DaysAndNegativeClock_KeepTheirOwnSigns()
    {
        var value = Read("3 days -01:30:00");

        Assert.Equal(3, value.Days);
        Assert.Equal(-5_400_000_000L, value.Microseconds);
    }
}
