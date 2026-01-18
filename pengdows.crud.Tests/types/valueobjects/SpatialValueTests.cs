using System;
using System.Text;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.valueobjects;

public static class SpatialValueTests
{
    [Fact]
    public static void Geometry_FromWellKnownBinary_PreservesMetadata()
    {
        var wkb = new byte[] { 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // minimal point body
        var geometry = Geometry.FromWellKnownBinary(wkb, srid: 3857);

        Assert.Equal(3857, geometry.Srid);
        Assert.Equal(SpatialFormat.WellKnownBinary, geometry.Format);
        Assert.True(geometry.WellKnownBinary.Span.SequenceEqual(wkb));
        Assert.Null(geometry.WellKnownText);
        Assert.Null(geometry.GeoJson);
    }

    [Fact]
    public static void Geography_FromGeoJson_ThrowsWhenEmpty()
    {
        var ex = Assert.Throws<ArgumentException>(() => Geography.FromGeoJson(string.Empty, 4326));
        Assert.Contains("GeoJSON", ex.Message);
    }

    [Fact]
    public static void SpatialValue_Equals_ComparesComponentValues()
    {
        var wkt = "POINT(1 2)";
        var lhs = Geometry.FromWellKnownText(wkt, 4326);
        var rhs = Geometry.FromWellKnownText("POINT(1 2)", 4326);
        var different = Geometry.FromWellKnownText("POINT(5 6)", 4326);

        Assert.Equal(lhs, rhs);
        Assert.True(lhs.Equals((object)rhs));
        Assert.NotEqual(lhs, different);
    }

    [Fact]
    public static void SpatialValue_ToString_FallsBackToGeoJsonOrBinary()
    {
        var geo = Geography.FromGeoJson("{\"type\":\"Point\",\"coordinates\":[0,0]}", 4326);
        Assert.Equal("{\"type\":\"Point\",\"coordinates\":[0,0]}", geo.ToString());

        var binary = Geometry.FromWellKnownBinary(new byte[] { 1, 2, 3 }, 0);
        var expected = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        Assert.Equal(expected, binary.ToString());
    }

    [Fact]
    public static void Geometry_WithProviderValue_PreservesExistingPayload()
    {
        var provider = new object();
        var geometry = Geometry.FromWellKnownText("POINT(0 0)", 4326).WithProviderValue(provider);

        Assert.Same(provider, geometry.ProviderValue);
    }

    [Fact]
    public static void HashCode_ChangesWhenBinaryDiffers()
    {
        var first = Geography.FromWellKnownBinary(new byte[] { 1, 2, 3 }, 0);
        var second = Geography.FromWellKnownBinary(new byte[] { 1, 2, 4 }, 0);

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
    }
}
