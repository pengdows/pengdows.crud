#region

using System;
using System.Buffers.Binary;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.converters;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class GeometryConverterCoverageTests
{
    [Fact]
    public void TryConvertFromProvider_ExtractsSridFromTextAndGeoJson()
    {
        var converter = new GeometryConverter();

        var wkt = "SRID=4326;POINT(1 2)";
        Assert.True(converter.TryConvertFromProvider(wkt, SupportedDatabase.PostgreSql, out var wktGeometry));
        Assert.Equal(4326, wktGeometry.Srid);

        var geoJson = "{\"type\":\"Point\",\"coordinates\":[1,2],\"srid\":3857}";
        Assert.True(converter.TryConvertFromProvider(geoJson, SupportedDatabase.PostgreSql, out var geoJsonGeometry));
        Assert.Equal(3857, geoJsonGeometry.Srid);
    }

    [Fact]
    public void TryConvertFromProvider_ExtractsSridFromEwkb()
    {
        var converter = new GeometryConverter();
        var bytes = BuildEwkbWithSrid(999);
        Assert.True(converter.TryConvertFromProvider(bytes, SupportedDatabase.PostgreSql, out var geometry));
        Assert.Equal(999, geometry.Srid);
    }

    [Fact]
    public void ExtractSrid_PrivateHelpers_HandleEdgeCases()
    {
        var extractText = typeof(GeometryConverter).GetMethod("ExtractSridFromText",
            BindingFlags.NonPublic | BindingFlags.Static);
        var extractGeoJson = typeof(GeometryConverter).GetMethod("ExtractSridFromGeoJson",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(extractText);
        Assert.NotNull(extractGeoJson);

        var emptyResult = ((int srid, string text))extractText!.Invoke(null, new object[] { string.Empty })!;
        Assert.Equal(0, emptyResult.srid);
        Assert.Equal(string.Empty, emptyResult.text);

        var malformedPrefix = ((int srid, string text))extractText.Invoke(null, new object[] { "SRID=;POINT(1 2)" })!;
        Assert.Equal(0, malformedPrefix.srid);
        Assert.Equal("SRID=;POINT(1 2)", malformedPrefix.text);

        var whiteSpaceGeoJson = (int)extractGeoJson!.Invoke(null, new object[] { "   " })!;
        Assert.Equal(0, whiteSpaceGeoJson);

        var missingColonGeoJson = (int)extractGeoJson.Invoke(null, new object[] { "{\"srid\" \"4326\"}" })!;
        Assert.Equal(0, missingColonGeoJson);
    }

    private static byte[] BuildEwkbWithSrid(int srid)
    {
        // Little-endian flag + type + srid flag + srid bytes
        var wkb = new byte[1 + 4 + 4 + 1];
        wkb[0] = 1; // little-endian
        // type with srid flag (0x20000000) and point (1)
        var type = 0x20000001u;
        BinaryPrimitives.WriteUInt32LittleEndian(wkb.AsSpan(1, 4), type);
        BinaryPrimitives.WriteInt32LittleEndian(wkb.AsSpan(5, 4), srid);
        return wkb;
    }
}
