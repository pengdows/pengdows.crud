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
}
