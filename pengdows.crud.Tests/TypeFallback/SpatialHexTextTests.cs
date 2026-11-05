using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A database that stores a spatial value as EWKB hex (Snowflake: "EWKT, else EWKB as hex") returns that hex text. The hex is
/// the value's WKB, SRID flag and all: it reads back with its SRID and geometry, not as WKT text with SRID 0.
/// </summary>
public class SpatialHexTextTests
{
    [Fact]
    public void EwkbHexReadsBackWithItsSridAndGeometry()
    {
        const string hex = "0101000020E6100000000000000000F03F0000000000000040";

        Assert.True(new GeometryConverter().TryConvertFromProvider(hex, SupportedDatabase.Snowflake, out var geometry));

        Assert.Equal(4326, geometry.Srid);
        Assert.Equal("POINT (1 2)", ExtendedWellKnownText.WellKnownTextOf(geometry));
    }

    [Fact]
    public void WellKnownTextIsStillTextNotHex()
    {
        Assert.True(new GeometryConverter().TryConvertFromProvider("POINT (1 2)", SupportedDatabase.Snowflake, out var geometry));

        Assert.Equal("POINT (1 2)", geometry.WellKnownText);
    }
}
