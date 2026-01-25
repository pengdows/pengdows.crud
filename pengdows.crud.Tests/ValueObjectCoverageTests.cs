using System;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class ValueObjectCoverageTests
{
    [Fact]
    public void PostgreSqlInterval_TimeSpanProperties_Behave()
    {
        var microseconds = 1_234_500L;
        var interval = new PostgreSqlInterval(1, 2, microseconds);
        var expected = TimeSpan.FromDays(2) + TimeSpan.FromTicks(microseconds * 10);
        Assert.Equal(expected, interval.ToTimeSpan());
        Assert.Equal(TimeSpan.FromTicks(microseconds * 10), interval.TimeComponent);
        Assert.Contains("months", interval.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlInterval_FromTimeSpan_PreservesPrecision()
    {
        var span = TimeSpan.FromDays(3) + TimeSpan.FromMinutes(5);
        var converted = PostgreSqlInterval.FromTimeSpan(span);
        Assert.Equal(3, converted.Days);
        Assert.Equal(span.Ticks / 10, converted.Microseconds);
        Assert.Equal(0, converted.Months);
    }

    [Fact]
    public void PostgreSqlInterval_EqualsAndHashCode_Work()
    {
        var first = new PostgreSqlInterval(0, 1, 2);
        var second = new PostgreSqlInterval(0, 1, 2);
        var other = new PostgreSqlInterval(1, 1, 2);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.True(first.Equals((object)second));
        Assert.False(first.Equals((object?)null));
    }

    [Fact]
    public void MacAddress_ParseDotFormat_ReturnsNormalizedString()
    {
        var result = MacAddress.Parse("0011.2233.4455");
        Assert.Equal("00:11:22:33:44:55", result.ToString());
    }

    [Fact]
    public void MacAddress_Parse_InvalidLength_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => MacAddress.Parse("0011:2233:445"));
    }

    [Fact]
    public void Range_Parse_CanonicalString()
    {
        var parsed = Range<int>.Parse("[1, 5)");
        Assert.Equal(1, parsed.Lower);
        Assert.Equal(5, parsed.Upper);
        Assert.True(parsed.IsLowerInclusive);
        Assert.False(parsed.IsUpperInclusive);
    }

    [Fact]
    public void Range_Parse_InvalidText_Throws()
    {
        Assert.Throws<FormatException>(() => Range<int>.Parse("invalid"));
    }

    [Fact]
    public void Range_Parse_DateTime_OpenStart()
    {
        var parsed = Range<DateTime>.Parse("(,2025-12-31]");
        Assert.True(parsed.HasUpperBound);
        Assert.Equal(new DateTime(2025, 12, 31), parsed.Upper);
        Assert.True(parsed.IsUpperInclusive);
    }

    [Fact]
    public void Range_ToString_IncludesBraces()
    {
        var range = new Range<int>(1, 2, false, true);
        Assert.Equal("(1, 2]", range.ToString());
    }

    [Fact]
    public void RowVersion_Ctor_InvalidLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RowVersion(new byte[7]));
    }

    [Fact]
    public void RowVersion_ToStringAndEquality_Work()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var version = new RowVersion(bytes);
        var cloned = RowVersion.FromBytes(bytes);
        Assert.Equal(version, cloned);
        Assert.Equal(version.GetHashCode(), cloned.GetHashCode());
        Assert.Equal(BitConverter.ToString(bytes).Replace("-", string.Empty, StringComparison.Ordinal),
            version.ToString());
    }
}