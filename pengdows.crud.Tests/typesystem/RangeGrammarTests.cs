using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-013: Range&lt;T&gt;.Parse and PostgreSqlRangeConverter each had a grammar, and they disagreed:
/// the converter read blank or bracket-less text as an empty range with no error, neither unquoted
/// PostgreSQL's quoted bounds (every timestamp range), and both wrote a DateTime bound in the invariant
/// culture's "MM/dd/yyyy HH:mm:ss", dropping its fraction. One grammar now reads every form, bounds
/// convert through TypeCoercionHelper, and the converter writes ISO bounds to the tick.
/// </summary>
public class RangeGrammarTests
{
    [Theory]
    [InlineData("[1,5)", 1, 5, true, false)]
    [InlineData("(1, 5]", 1, 5, false, true)]
    [InlineData("[\"1\",\"5\")", 1, 5, true, false)]
    [InlineData("  [ 1 , 5 )  ", 1, 5, true, false)]
    public void IntRanges_Parse(string text, int lower, int upper, bool lowerInclusive, bool upperInclusive)
    {
        foreach (var range in new[] { Range<int>.Parse(text), Read<int>(text) })
        {
            Assert.Equal(lower, range.Lower);
            Assert.Equal(upper, range.Upper);
            Assert.Equal(lowerInclusive, range.IsLowerInclusive);
            Assert.Equal(upperInclusive, range.IsUpperInclusive);
        }
    }

    [Fact]
    public void UnboundedAndEmpty_Parse()
    {
        var open = Range<int>.Parse("(,5]");
        Assert.False(open.HasLowerBound);
        Assert.Equal(5, open.Upper);
        Assert.False(Range<int>.Parse("[1,)").HasUpperBound);
        Assert.True(Range<int>.Parse("EMPTY").IsEmptyRange);
        Assert.True(Read<int>("empty").IsEmptyRange);
    }

    // PostgreSQL quotes timestamp bounds: ["2026-10-05 13:45:30.1234567","2026-10-06 00:00:00").
    [Fact]
    public void QuotedTimestampBounds_ParseToTheTick()
    {
        const string text = "[\"2026-10-05 13:45:30.1234567\",\"2026-10-06 00:00:00\")";
        var expected = new DateTime(2026, 10, 5, 13, 45, 30).AddTicks(1234567);

        Assert.Equal(expected, Range<DateTime>.Parse(text).Lower);
        Assert.Equal(expected, Read<DateTime>(text).Lower);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("[1;5)")]
    [InlineData("[1,5")]
    [InlineData("{1,5)")]
    [InlineData("[x,5)")]
    [InlineData("[1,5,7)")]
    [InlineData("[\"1,5)")]
    public void OtherText_Fails_WithoutEchoingIt(string text)
    {
        var ex = Assert.ThrowsAny<FormatException>(() => Range<int>.Parse(text));
        Assert.DoesNotContain(text, ex.Message, StringComparison.Ordinal);
        Assert.Null(new PostgreSqlRangeConverter<int>().FromProviderValue(text, SupportedDatabase.PostgreSql));
    }

    // Blank is no range on the read path (the converter read it as empty); Parse keeps its ArgumentException.
    [Fact]
    public void BlankText_IsNoValue()
    {
        Assert.Null(new PostgreSqlRangeConverter<int>().FromProviderValue("  ", SupportedDatabase.PostgreSql));
        Assert.Throws<ArgumentException>(() => Range<int>.Parse(" "));
    }

    // What the converter writes as text it reads back exactly, and Parse reads ToString's form too.
    [Fact]
    public void WrittenText_ReadsBackExactly()
    {
        var at = new DateTime(2026, 10, 5, 13, 45, 30).AddTicks(1234567);
        var dates = new Range<DateTime>(at, at.AddDays(1), true, false);
        var offsets = new Range<DateTimeOffset>(new DateTimeOffset(at, TimeSpan.FromHours(2)), null, false, false);
        var days = new Range<DateOnly>(new DateOnly(2026, 1, 2), new DateOnly(2026, 3, 4), true, true);
        var amounts = new Range<decimal>(1.25m, 99.999m, true, false);

        Assert.Equal(dates, RoundTrip(dates));
        Assert.Equal(offsets, RoundTrip(offsets));
        Assert.Equal(days, RoundTrip(days));
        Assert.Equal(amounts, RoundTrip(amounts));
        Assert.Equal(new Range<int>(1, 5, true, false), Range<int>.Parse(new Range<int>(1, 5, true, false).ToString()));
    }

    private static Range<T> RoundTrip<T>(Range<T> range) where T : struct
    {
        // The PostgreSQL family without Npgsql loaded (as here) gets the canonical text.
        var converter = new PostgreSqlRangeConverter<T>();
        var text = Assert.IsType<string>(converter.ToProviderValue(range, SupportedDatabase.PostgreSql));
        return (Range<T>)converter.FromProviderValue(text, SupportedDatabase.PostgreSql)!;
    }

    private static Range<T> Read<T>(string text) where T : struct =>
        (Range<T>)new PostgreSqlRangeConverter<T>().FromProviderValue(text, SupportedDatabase.PostgreSql)!;
}
