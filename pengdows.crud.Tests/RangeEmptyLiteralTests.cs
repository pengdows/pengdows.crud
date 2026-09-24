#region

using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// PostgreSQL writes an empty range as the literal <c>empty</c>; both range parsers must accept it
/// instead of treating it as a bracketed range.
/// </summary>
public class RangeEmptyLiteralTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("EMPTY")]
    [InlineData("  empty  ")]
    public void RangeParse_EmptyLiteral_ReturnsEmpty(string text)
    {
        Assert.Equal(Range<int>.Empty, Range<int>.Parse(text));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData(" Empty ")]
    public void Converter_EmptyLiteral_ReturnsEmpty(string text)
    {
        var converter = new PostgreSqlRangeConverter<int>();

        Assert.True(converter.TryConvertFromProvider(text, SupportedDatabase.PostgreSql, out var result));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void RangeParse_BracketedRange_StillParses()
    {
        var range = Range<int>.Parse("[1,5)");

        Assert.Equal(1, range.Lower);
        Assert.Equal(5, range.Upper);
    }

    // Range<T>.Empty used to be default(Range<T>), the same value as an unbounded "(,)" range, so
    // writing Empty stored "all values". Empty is now its own value; IsEmpty still reports true
    // for an unbounded range too (unchanged on 2.0.x).
    [Fact]
    public void Empty_IsDistinctFromUnboundedRange()
    {
        var unbounded = new Range<int>(null, null, false, false);

        Assert.NotEqual(Range<int>.Empty, unbounded);
        Assert.NotEqual(Range<int>.Empty, default);
        Assert.True(Range<int>.Empty.IsEmpty);
        Assert.True(unbounded.IsEmpty);
    }

    [Fact]
    public void Empty_ToString_IsEmptyLiteral()
    {
        Assert.Equal("empty", Range<int>.Empty.ToString());
    }

    [Fact]
    public void Converter_WritesEmptyAsEmptyLiteral_WhenNpgsqlIsNotLoaded()
    {
        var converter = new PostgreSqlRangeConverter<int>();

        Assert.Equal("empty", converter.ToProviderValue(Range<int>.Empty, SupportedDatabase.PostgreSql));
        Assert.Equal("(,)", converter.ToProviderValue(new Range<int>(null, null, false, false), SupportedDatabase.PostgreSql));
    }

    [Fact]
    public void Parse_EmptyLiteral_RoundTripsThroughToString()
    {
        Assert.Equal(Range<int>.Empty, Range<int>.Parse(Range<int>.Empty.ToString()));
    }
}
