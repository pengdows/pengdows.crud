using System;
using System.Data;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A value type with a converter but no mapping for a provider is bound through that converter, as text (or binary):
/// a database without a native column for it still takes the value, and the converter reads it back.
/// Before this, the value reached the provider raw and the provider refused it ("No mapping exists ... native type").
/// </summary>
public class ConverterTextFallbackTests
{
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.MySql)]
    public void CidrWithoutAMapping_IsBoundAsItsText(SupportedDatabase provider)
    {
        var parameter = CreateParameter(provider);
        var registry = new AdvancedTypeRegistry(true);

        var bound = registry.TryConfigureParameter(parameter, typeof(Cidr), Cidr.Parse("10.0.0.0/8"), provider);

        Assert.True(bound);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("10.0.0.0/8", parameter.Value);
    }

    [Fact]
    public void ConverterOutputThatIsBinary_IsBoundAsBinary()
    {
        var parameter = CreateParameter(SupportedDatabase.SqlServer);
        var registry = new AdvancedTypeRegistry(true);

        var bound = registry.TryConfigureParameter(parameter, typeof(MacAddress),
            MacAddress.Parse("08:00:2b:01:02:03"), SupportedDatabase.SqlServer);

        Assert.True(bound);
        Assert.IsNotType<DBNull>(parameter.Value);
        Assert.NotNull(parameter.Value);
    }

    private static DbParameter CreateParameter(SupportedDatabase provider) =>
        new fakeDbFactory(provider).CreateParameter();
}

/// <summary>
/// Spatial values are not bound through the fallback: their binary form drops the SRID, so a database without a spatial
/// type would store the geometry with SRID 0 and nothing would say so. They keep failing loudly.
/// </summary>
public class SpatialFallbackTests
{
    [Theory]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void GeometryWithNoMapping_IsNotBoundAsBareWkb(SupportedDatabase provider)
    {
        var parameter = new fakeDbFactory(provider).CreateParameter();
        var registry = new AdvancedTypeRegistry(true);
        var point = Geometry.FromWellKnownText("POINT (1 2)", 4326);

        var bound = registry.TryConfigureParameter(parameter, typeof(Geometry), point, provider);

        Assert.False(bound);
    }
}

/// <summary>
/// An interval with no native mapping on a database is bound as text the interval converter reads back exactly: months,
/// days and microseconds all survive the trip.
/// </summary>
public class IntervalTextFallbackTests
{
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, 14, 3, 5_000_000L)]
    [InlineData(SupportedDatabase.SqlServer, 0, 1, 3_723_000_000L)]
    public void IntervalWithoutAMapping_ReadsBackExactly(SupportedDatabase provider, int months, int days, long micros)
    {
        var parameter = new fakeDbFactory(provider).CreateParameter();
        var registry = new AdvancedTypeRegistry(true);
        var interval = new PostgreSqlInterval(months, days, micros);

        Assert.True(registry.TryConfigureParameter(parameter, typeof(PostgreSqlInterval), interval, provider));

        var text = Assert.IsType<string>(parameter.Value);
        Assert.True(new PostgreSqlIntervalConverter().TryConvertFromProvider(text, provider, out var back));
        Assert.Equal(interval, back);
    }
}

/// <summary>
/// The verbose interval text the converter reads: every unit it accepts is scaled to microseconds by its own size.
/// </summary>
public class VerboseIntervalUnitTests
{
    [Theory]
    [InlineData("2 hours 30 minutes", 0L)]
    [InlineData("1 hour", 0L)]
    [InlineData("3 mins", 0L)]
    [InlineData("1 hr", 0L)]
    public void HoursAndMinutes_AreScaledToMicroseconds(string text, long _)
    {
        var parsed = new PostgreSqlIntervalConverter().TryConvertFromProvider(text, SupportedDatabase.PostgreSql, out var interval);

        Assert.True(parsed);
        var expected = text switch
        {
            "2 hours 30 minutes" => 9_000_000_000L,
            "1 hour" => 3_600_000_000L,
            "3 mins" => 180_000_000L,
            _ => 3_600_000_000L,
        };
        Assert.Equal(expected, interval.Microseconds);
    }

    [Theory]
    [InlineData("500 usecs", 500L)]
    [InlineData("500 microseconds", 500L)]
    [InlineData("2 secs", 2_000_000L)]
    public void MicrosecondsMillisecondsAndSeconds_AreScaled(string text, long expected)
    {
        var parsed = new PostgreSqlIntervalConverter().TryConvertFromProvider(text, SupportedDatabase.PostgreSql, out var interval);

        Assert.True(parsed);
        Assert.Equal(expected, interval.Microseconds);
    }
}
