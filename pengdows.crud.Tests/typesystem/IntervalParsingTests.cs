using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-012: the interval value objects and their converters each had a parser that skipped letters it
/// didn't know and dropped numbers with no unit, so text in another form read as a wrong value with no
/// error: Oracle's "+0001-02" (which the converter itself writes) as 0 months, "P1DT2H3M4S" as 3
/// months, "P1Y2M" as 0 days, lower-case "p1dt2h3m4s" as 1 day. One strict parser per type now reads
/// every form the library writes or a database returns, exactly to the tick, and anything else fails.
/// </summary>
public class IntervalParsingTests
{
    [Theory]
    [InlineData("P1Y2M", 14)]
    [InlineData("p1y2m", 14)]
    [InlineData("P2M", 2)]
    [InlineData("P1Y", 12)]
    [InlineData("-P1Y2M", -14)]
    [InlineData("P-1Y-2M", -14)]
    [InlineData("+0001-02", 14)]
    [InlineData("1-2", 14)]
    [InlineData("-1-02", -14)]
    [InlineData("1 years 2 months", 14)]
    [InlineData("1 year 2 mons", 14)]
    [InlineData("2 mons", 2)]
    [InlineData("-1 years -2 mons", -14)]
    public void YearMonth_ReadsEveryForm(string text, int totalMonths)
    {
        Assert.Equal(totalMonths, IntervalYearMonth.Parse(text).TotalMonths);
        Assert.Equal(totalMonths,
            ((IntervalYearMonth)new IntervalYearMonthConverter().FromProviderValue(text, SupportedDatabase.Oracle)!).TotalMonths);
    }

    [Theory]
    [InlineData("P1DT2H3M4S")]
    [InlineData("P1D")]
    [InlineData("1 02:03:04")]
    [InlineData("P")]
    [InlineData("P1X")]
    [InlineData("1 year 2 days")]
    [InlineData("abc")]
    [InlineData("1-2-3")]
    public void YearMonth_OtherText_Fails(string text)
    {
        Assert.Throws<FormatException>(() => IntervalYearMonth.Parse(text));
        Assert.Null(new IntervalYearMonthConverter().FromProviderValue(text, SupportedDatabase.Oracle));
    }

    [Theory]
    [InlineData("P1DT2H3M4S", 1, "02:03:04")]
    [InlineData("p1dt2h3m4s", 1, "02:03:04")]
    [InlineData("PT0.1234567S", 0, "00:00:00.1234567")]
    [InlineData("PT1.123456789S", 0, "00:00:01.1234567")]
    [InlineData("P2D", 2, "00:00:00")]
    [InlineData("PT26H", 1, "02:00:00")]
    [InlineData("-P1DT2H", -1, "-02:00:00")]
    [InlineData("+000000001 02:03:04.123456", 1, "02:03:04.1234560")]
    [InlineData("-1 02:03:04", -1, "-02:03:04")]
    [InlineData("1 02:03", 1, "02:03:00")]
    [InlineData("02:03:04.5", 0, "02:03:04.5000000")]
    [InlineData("1 days 02:03:04", 1, "02:03:04")]
    [InlineData("1 day 02:03:04.25", 1, "02:03:04.2500000")]
    [InlineData("3 days", 3, "00:00:00")]
    public void DaySecond_ReadsEveryForm_ToTheTick(string text, int days, string time)
    {
        var expected = TimeSpan.FromDays(days) + TimeSpan.Parse(time, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, IntervalDaySecond.Parse(text).TotalTime);
        Assert.Equal(expected,
            ((IntervalDaySecond)new IntervalDaySecondConverter().FromProviderValue(text, SupportedDatabase.Oracle)!).TotalTime);
    }

    [Theory]
    [InlineData("P1Y2M")]
    [InlineData("P1M")]
    [InlineData("P")]
    [InlineData("PT")]
    [InlineData("P1DT")]
    [InlineData("1-2")]
    [InlineData("1 02:61:00")]
    [InlineData("abc")]
    [InlineData("1 day 2 hours")]
    public void DaySecond_OtherText_Fails(string text)
    {
        Assert.Throws<FormatException>(() => IntervalDaySecond.Parse(text));
        Assert.Null(new IntervalDaySecondConverter().FromProviderValue(text, SupportedDatabase.Oracle));
    }

    // Blank text is no interval on the read path (COR-002); Parse keeps its documented zero for blank.
    [Fact]
    public void BlankText_ReadsAsNoValue_ParseStillGivesZero()
    {
        Assert.Null(new IntervalYearMonthConverter().FromProviderValue("  ", SupportedDatabase.Oracle));
        Assert.Null(new IntervalDaySecondConverter().FromProviderValue("", SupportedDatabase.Oracle));
        Assert.Equal(0, IntervalYearMonth.Parse(" ").TotalMonths);
        Assert.Equal(TimeSpan.Zero, IntervalDaySecond.Parse("").TotalTime);
    }

    // What the converters write, they read back exactly.
    [Theory]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void WrittenText_ReadsBackExactly(SupportedDatabase provider)
    {
        var ym = new IntervalYearMonth(-3, -5);
        var ds = IntervalDaySecond.FromTimeSpan(new TimeSpan(-1, -2, -3, -4).Add(TimeSpan.FromTicks(-1234567)));
        var ymConverter = new IntervalYearMonthConverter();
        var dsConverter = new IntervalDaySecondConverter();

        foreach (var written in new[] { ymConverter.ToProviderValue(ym, provider), (object)ym.ToString() })
        {
            if (written is string text)
            {
                Assert.Equal(ym.TotalMonths, ((IntervalYearMonth)ymConverter.FromProviderValue(text, provider)!).TotalMonths);
            }
        }

        // Oracle's literal carries 6 fraction digits (its default INTERVAL DAY TO SECOND(6)), truncated
        // so Oracle never rounds; everything else carries all 7.
        var oracleTicks = ds.TotalTime.Ticks / 10 * 10;
        var expectedWritten = provider == SupportedDatabase.Oracle ? TimeSpan.FromTicks(oracleTicks) : ds.TotalTime;
        foreach (var (written, expected) in new[] { (dsConverter.ToProviderValue(ds, provider), expectedWritten), ((object?)ds.ToString(), ds.TotalTime) })
        {
            if (written is string text)
            {
                Assert.Equal(expected, ((IntervalDaySecond)dsConverter.FromProviderValue(text, provider)!).TotalTime);
            }
        }
    }
}
