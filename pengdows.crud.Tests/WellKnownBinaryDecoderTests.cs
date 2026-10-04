using System;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-021: Oracle builds SDO_GEOMETRY from text, so a geometry held as WKB is written as WKT. The
/// decoder is the inverse of <see cref="WellKnownTextEncoder"/>: 2D, both byte orders, Z/M refused.
/// </summary>
public class WellKnownBinaryDecoderTests
{
    [Theory]
    [InlineData("POINT (1.5 -2.25)")]
    [InlineData("POINT EMPTY")]
    [InlineData("LINESTRING (0 0, 1 1, 2 0.5)")]
    [InlineData("LINESTRING EMPTY")]
    [InlineData("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 3 2, 3 3, 2 2))")]
    [InlineData("MULTIPOINT ((1 2), (3 4))")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")]
    [InlineData("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))")]
    [InlineData("GEOMETRYCOLLECTION EMPTY")]
    public void Decode_IsTheInverseOfEncode(string wkt)
    {
        var wkb = WellKnownTextEncoder.Encode(wkt);

        var decoded = WellKnownBinaryDecoder.ToWellKnownText(wkb);

        Assert.Equal(wkb, WellKnownTextEncoder.Encode(decoded));
    }

    [Fact]
    public void Decode_KeepsEveryDoubleExactly()
    {
        var x = 0.00037037036703703495;
        var y = -1.7976931348623157E+308;
        var wkb = WellKnownTextEncoder.Encode($"POINT ({x:R} {y:R})");

        var decoded = WellKnownBinaryDecoder.ToWellKnownText(wkb);

        Assert.Equal(wkb, WellKnownTextEncoder.Encode(decoded));
        Assert.Contains("0.00037037036703703495", decoded);
    }

    [Fact]
    public void Decode_ReadsBigEndianInput()
    {
        // Oracle's own WKB (and many servers') is big-endian.
        var bigEndian = Convert.FromHexString("00000000013FF8000000000000C002000000000000");

        Assert.Equal("POINT (1.5 -2.25)", WellKnownBinaryDecoder.ToWellKnownText(bigEndian));
    }

    [Theory]
    [InlineData("01E9030000000000000000F03F00000000000000400000000000000840")] // ISO POINT Z (1001)
    [InlineData("0101000080000000000000F03F00000000000000400000000000000840")] // EWKB Z flag
    public void Decode_RefusesZAndMRatherThanDroppingThem(string hex)
    {
        Assert.Throws<NotSupportedException>(() => WellKnownBinaryDecoder.ToWellKnownText(Convert.FromHexString(hex)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("0101000000000000000000F03F")]
    public void Decode_TruncatedInput_FailsAsFormatException(string hex)
    {
        Assert.Throws<FormatException>(() => WellKnownBinaryDecoder.ToWellKnownText(Convert.FromHexString(hex)));
    }
}
