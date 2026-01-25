using System;
using System.Buffers.Binary;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class GeographyConverterBranchTests
{
    [Fact]
    public void FromBinary_UsesDefaultSridWhenMissing()
    {
        var converter = new GeographyConverter();
        var shortBytes = new byte[] { 1, 2, 3, 4 };

        Assert.True(converter.TryConvertFromProvider(shortBytes, SupportedDatabase.PostgreSql, out var spatial));
        Assert.Equal(4326, spatial.Srid);
        Assert.Equal(shortBytes, spatial.WellKnownBinary.ToArray());
    }

    [Fact]
    public void FromBinary_ReadsSridFromEwkb_LittleEndian()
    {
        var converter = new GeographyConverter();
        var srid = 3857;
        var bytes = CreateEwkbBytes(true, srid);

        Assert.True(converter.TryConvertFromProvider(bytes, SupportedDatabase.PostgreSql, out var spatial));
        Assert.Equal(srid, spatial.Srid);
    }

    [Fact]
    public void FromBinary_ReadsSridFromEwkb_BigEndian()
    {
        var converter = new GeographyConverter();
        var srid = 4326;
        var bytes = CreateEwkbBytes(false, srid);

        Assert.True(converter.TryConvertFromProvider(bytes, SupportedDatabase.PostgreSql, out var spatial));
        Assert.Equal(srid, spatial.Srid);
    }

    [Fact]
    public void FromText_ParsesSridPrefix()
    {
        var converter = new GeographyConverter();

        Assert.True(converter.TryConvertFromProvider("SRID=3857;POINT(1 2)", SupportedDatabase.PostgreSql,
            out var spatial));
        Assert.Equal(3857, spatial.Srid);

        Assert.True(converter.TryConvertFromProvider("POINT(1 2)", SupportedDatabase.PostgreSql, out var defaultSrid));
        Assert.Equal(4326, defaultSrid.Srid);

        Assert.True(converter.TryConvertFromProvider("SRID=bad;POINT(1 2)", SupportedDatabase.PostgreSql,
            out var invalidSrid));
        Assert.Equal(4326, invalidSrid.Srid);
    }

    [Fact]
    public void FromGeoJson_ParsesSridProperty()
    {
        var converter = new GeographyConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[0,0],\"srid\":3857}";
        var missing = "{\"type\":\"Point\",\"coordinates\":[0,0]}";

        Assert.True(converter.TryConvertFromProvider(json, SupportedDatabase.PostgreSql, out var spatial));
        Assert.Equal(3857, spatial.Srid);

        Assert.True(converter.TryConvertFromProvider(missing, SupportedDatabase.PostgreSql, out var fallback));
        Assert.Equal(4326, fallback.Srid);
    }

    [Fact]
    public void FromGeoJson_InvalidSrid_FallsBack()
    {
        var converter = new GeographyConverter();
        var invalid = "{\"type\":\"Point\",\"coordinates\":[0,0],\"srid\":\"bad\"}";

        Assert.True(converter.TryConvertFromProvider(invalid, SupportedDatabase.PostgreSql, out var spatial));
        Assert.Equal(4326, spatial.Srid);
    }

    private static byte[] CreateEwkbBytes(bool littleEndian, int srid)
    {
        const uint sridFlag = 0x20000000;
        var type = sridFlag | 1u;
        var bytes = new byte[9];
        bytes[0] = littleEndian ? (byte)1 : (byte)0;
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1, 4), type);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5, 4), srid);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1, 4), type);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(5, 4), srid);
        }

        return bytes;
    }
}