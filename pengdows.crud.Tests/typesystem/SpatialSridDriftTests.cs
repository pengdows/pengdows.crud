using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-011: GeographyConverter kept its own copy of the EWKB SRID helper, which read the SRID but
/// left the flag and the 4 SRID bytes in the WKB (GeometryConverter's copy was fixed for TYPE-002).
/// EWKB read as a Geography must give the same plain WKB and SRID a Geometry gets.
/// </summary>
public class SpatialSridDriftTests
{
    private static readonly byte[] Ewkb = Convert.FromHexString("0101000020E6100000000000000000F03F0000000000000040");
    private const string PlainWkb = "0101000000000000000000F03F0000000000000040";

    [Fact]
    public void GeographyFromEwkb_IsPlainWkbWithTheSrid_LikeGeometry()
    {
        var geometry = (Geometry)TypeCoercionHelper.Coerce(Ewkb, typeof(byte[]), typeof(Geometry))!;
        var geography = (Geography)TypeCoercionHelper.Coerce(Ewkb, typeof(byte[]), typeof(Geography))!;
        var converted = (Geography)new GeographyConverter().FromProviderValue(Ewkb, SupportedDatabase.PostgreSql)!;

        Assert.Equal(PlainWkb, Convert.ToHexString(geometry.WellKnownBinary.Span));
        foreach (var g in new[] { geography, converted })
        {
            Assert.Equal(PlainWkb, Convert.ToHexString(g.WellKnownBinary.Span));
            Assert.Equal(4326, g.Srid);
        }
    }

    // Found by TypeCompletenessTests: SingleStore takes spatial values as WKT and refused one built from
    // WKB (any value read from another database) instead of writing its WKB as WKT, as Oracle's EWKT
    // does. Text with an SRID prefix is written without it.
    [Theory]
    [InlineData("wkb")]
    [InlineData("wkt")]
    [InlineData("ewkt")]
    public void SingleStore_WritesEverySpatialValueAsPlainWkt(string source)
    {
        var wkb = Convert.FromHexString(PlainWkb);
        Geometry geometry = source switch
        {
            "wkb" => Geometry.FromWellKnownBinary(wkb, 4326),
            "wkt" => Geometry.FromWellKnownText("POINT (1 2)", 4326),
            _ => Geometry.FromWellKnownText("SRID=4326;POINT (1 2)", 4326)
        };
        Geography geography = source == "wkb" ? Geography.FromWellKnownBinary(wkb, 4326) : Geography.FromWellKnownText("POINT (1 2)", 4326);

        Assert.Equal("POINT (1 2)", new GeometryConverter().ToProviderValue(geometry, SupportedDatabase.SingleStore));
        Assert.Equal("POINT (1 2)", new GeographyConverter().ToProviderValue(geography, SupportedDatabase.SingleStore));
    }
}
