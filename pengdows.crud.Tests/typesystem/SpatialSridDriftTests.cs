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
}
