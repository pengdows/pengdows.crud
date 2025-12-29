using System;
using System.Text;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class SpatialValueTests
{
    [Fact]
    public void Constructor_ThrowsForNegativeSrid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TestSpatialValue(-1, SpatialFormat.WellKnownText, ReadOnlyMemory<byte>.Empty, "POINT()", null));
    }

    [Fact]
    public void ToString_RespectsFormat()
    {
        var wkt = new TestSpatialValue(4326, SpatialFormat.WellKnownText, ReadOnlyMemory<byte>.Empty, "POINT(0 0)", null);
        var geoJson = new TestSpatialValue(4326, SpatialFormat.GeoJson, ReadOnlyMemory<byte>.Empty, null, "{\"type\":\"Point\"}");
        var wkb = new TestSpatialValue(4326, SpatialFormat.WellKnownBinary,
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("binary")), null, null);

        Assert.Equal("POINT(0 0)", wkt.ToString());
        Assert.Equal("{\"type\":\"Point\"}", geoJson.ToString());
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("binary")), wkb.ToString());
    }

    [Fact]
    public void EqualsAndHashCode_ConsiderAllFields()
    {
        var bytes = new ReadOnlyMemory<byte>(new byte[] { 0, 1, 2 });
        var left = new TestSpatialValue(123, SpatialFormat.WellKnownBinary, bytes, "LINESTRING()", null);
        var right = new TestSpatialValue(123, SpatialFormat.WellKnownBinary, bytes, "LINESTRING()", null);
        var different = new TestSpatialValue(124, SpatialFormat.WellKnownBinary, ReadOnlyMemory<byte>.Empty, "LINESTRING()", null);

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.False(left.Equals(null));
        Assert.False(left.Equals(different));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    private sealed class TestSpatialValue : SpatialValue
    {
        public TestSpatialValue(
            int srid,
            SpatialFormat format,
            ReadOnlyMemory<byte> wkb,
            string? wkt,
            string? geoJson) : base(srid, format, wkb, wkt, geoJson)
        {
        }
    }
}
