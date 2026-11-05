using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Npgsql reports a column's data type with its type modifier: an unconstrained INTERVAL as "interval", and an
/// INTERVAL YEAR TO MONTH column as "interval(65535)". The typed interval reader must match both, or a year-month
/// column falls back to GetValue, whose TimeSpan refuses months (confirmed live on PostgreSQL and YugabyteDB).
/// </summary>
public class NpgsqlTypedDataTypeNameTests
{
    [Theory]
    [InlineData("interval", "interval")]
    [InlineData("interval(65535)", "interval")]
    [InlineData("INTERVAL(3)", "interval")]
    [InlineData("interval(6)", "interval")]
    public void ReportedNameWithAModifierMatchesTheBaseName(string reported, string expected)
    {
        Assert.True(NpgsqlTypedFieldReader.DataTypeNameMatches(reported, expected));
    }

    [Theory]
    [InlineData("timestamp(6)", "interval")]
    [InlineData("intervalx", "interval")]
    [InlineData("interval[]", "interval")]
    [InlineData("", "interval")]
    public void OtherNamesDoNotMatch(string reported, string expected)
    {
        Assert.False(NpgsqlTypedFieldReader.DataTypeNameMatches(reported, expected));
    }
}
