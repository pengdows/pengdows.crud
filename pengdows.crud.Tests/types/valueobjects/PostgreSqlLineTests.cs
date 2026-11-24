using System;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using pengdows.crud.enums;
using Xunit;

namespace NpgsqlTypes
{
    public sealed class NpgsqlLine
    {
        public override string ToString() => "{1,1,0}";
    }
}

namespace pengdows.crud.Tests.GeometricValues
{
public class PostgreSqlLineTests
{
    [Fact]
    public void Parse_ReadsTheThreeCoefficients()
    {
        var line = PostgreSqlLine.Parse("{1,-2,3}");

        Assert.Equal(1, line.A);
        Assert.Equal(-2, line.B);
        Assert.Equal(3, line.C);
    }

    [Fact]
    public void ToString_WritesPostgreSqlsLineText()
    {
        Assert.Equal("{1,-2,3}", new PostgreSqlLine(1, -2, 3).ToString());
    }

    [Theory]
    [InlineData("(1,2)")]
    [InlineData("{1,2}")]
    [InlineData("{1,NaN,3}")]
    [InlineData("{1,2,3")]
    [InlineData("")]
    public void TryParse_RefusesAnythingThatIsNotThreeFiniteCoefficients(string text)
    {
        Assert.False(PostgreSqlLine.TryParse(text, out _));
    }

    [Fact]
    public void Constructor_RefusesNonFiniteCoefficients()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgreSqlLine(double.NaN, 1, 1));
    }

    [Fact]
    public void Converter_PassesTheValueThrough()
    {
        var line = new PostgreSqlLine(1, 1, 0);

        Assert.True(new PostgreSqlLineConverter().TryConvertFromProvider(line, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(line, result);
    }

    [Fact]
    public void Converter_ReadsTheProvidersText()
    {
        Assert.True(new PostgreSqlLineConverter().TryConvertFromProvider("{2,3,4}", SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(new PostgreSqlLine(2, 3, 4), result);
    }

    [Fact]
    public void Converter_ReadsNpgsqlsOwnLineThroughItsText()
    {
        Assert.True(new PostgreSqlLineConverter().TryConvertFromProvider(new NpgsqlTypes.NpgsqlLine(), SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(new PostgreSqlLine(1, 1, 0), result);
    }

    [Fact]
    public void Converter_WritesTheTextForThePostgreSqlProvider()
    {
        var converted = new PostgreSqlLineConverter().ToProviderValue(new PostgreSqlLine(1, -2, 3), SupportedDatabase.PostgreSql);

        Assert.Equal("{1,-2,3}", converted);
    }
}
}
