using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests
{
public class PostgreSqlIntervalConverterBranchTests
{
    [Fact]
    public void ConvertToProvider_ReturnsValueOutsidePostgres()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(months: 1, days: 2, microseconds: 3);

        var result = converter.ToProviderValue(interval, SupportedDatabase.SqlServer);

        Assert.Equal(interval, result);
    }

    [Fact]
    public void ConvertToProvider_FormatsIso8601ForPostgres()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(months: 0, days: 0, microseconds: 0);

        var result = converter.ToProviderValue(interval, SupportedDatabase.PostgreSql);

        Assert.Equal("P0D", result);
    }

    [Fact]
    public void TryConvertFromProvider_HandlesIntervalTimeSpanAndString()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(months: 2, days: 3, microseconds: 4);

        Assert.True(converter.TryConvertFromProvider(interval, SupportedDatabase.PostgreSql, out var same));
        Assert.Equal(interval, same);

        Assert.True(converter.TryConvertFromProvider(TimeSpan.FromHours(1), SupportedDatabase.PostgreSql, out var fromTime));
        Assert.Equal(0, fromTime.Months);
        Assert.Equal(0, fromTime.Days);
        Assert.Equal(TimeSpan.FromHours(1).Ticks / 10, fromTime.Microseconds);

        Assert.True(converter.TryConvertFromProvider("P1M2DT3H4M5S", SupportedDatabase.PostgreSql, out var parsed));
        Assert.Equal(1, parsed.Months);
        Assert.Equal(2, parsed.Days);
        Assert.Equal(11_045_000_000, parsed.Microseconds);
    }

    [Fact]
    public void TryConvertFromProvider_HandlesNpgsqlTimeSpan()
    {
        var converter = new PostgreSqlIntervalConverter();
        var npgsql = new NpgsqlTypes.NpgsqlTimeSpan
        {
            Months = 1,
            Days = 2,
            Ticks = TimeSpan.FromSeconds(3).Ticks
        };

        Assert.True(converter.TryConvertFromProvider(npgsql, SupportedDatabase.PostgreSql, out var result));
        Assert.Equal(1, result.Months);
        Assert.Equal(2, result.Days);
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks / 10, result.Microseconds);
    }

    [Fact]
    public void TryConvertFromProvider_Invalid_ReturnsFalse()
    {
        var converter = new PostgreSqlIntervalConverter();
        Assert.False(converter.TryConvertFromProvider(new object(), SupportedDatabase.PostgreSql, out _));
    }
}
}

namespace NpgsqlTypes
{
    public sealed class NpgsqlTimeSpan
    {
        public int Months { get; set; }
        public int Days { get; set; }
        public long Ticks { get; set; }
    }
}
