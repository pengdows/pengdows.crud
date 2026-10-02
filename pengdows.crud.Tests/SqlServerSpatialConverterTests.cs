#region

using System;
using System.Buffers.Binary;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Types;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// SQL Server spatial conversion against the real Microsoft.SqlServer.Types API: SqlBytes/SqlChars
/// live in System.Data.SqlTypes, STGeomFromWKB/STGeomFromText take an int SRID, STAsBinary returns
/// SqlBytes and STSrid is a SqlInt32.
/// </summary>
public class SqlServerSpatialConverterTests
{
    private static byte[] Wkb(string wkt, int srid) =>
        SqlGeometry.STGeomFromText(new SqlChars(wkt.ToCharArray()), srid).STAsBinary().Value;

    // The parameter a spatial column binds: a big-endian SRID, then WKB, which the gateway SQL
    // hands to STGeomFromWKB. Rebuilt here with the real library's STGeomFromWKB. Only points:
    // off Windows the library throws PlatformNotSupportedException validating any other shape
    // (its validation is native code), which is why pengdows.crud doesn't depend on it.
    private static SqlGeometry ServerGeometry(object? argument)
    {
        var bytes = Assert.IsType<byte[]>(argument);
        return SqlGeometry.STGeomFromWKB(new SqlBytes(bytes[4..]), BinaryPrimitives.ReadInt32BigEndian(bytes));
    }

    [Fact]
    public void Geometry_FromWkb_WritesSridPrefixedWkb()
    {
        var converter = new GeometryConverter();
        var value = Geometry.FromWellKnownBinary(Wkb("POINT(1 2)", 3857), 3857);

        var server = ServerGeometry(converter.ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(3857, server.STSrid.Value);
        Assert.Equal("POINT (1 2)", server.STAsText().ToSqlString().Value);
    }

    [Fact]
    public void Geometry_FromWkt_WritesSridPrefixedWkb()
    {
        var converter = new GeometryConverter();
        var value = Geometry.FromWellKnownText("POINT(3 4)", 3857);

        var server = ServerGeometry(converter.ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(3857, server.STSrid.Value);
        Assert.Equal("POINT (3 4)", server.STAsText().ToSqlString().Value);
    }

    [Fact]
    public void Geography_FromWkt_WritesSridPrefixedWkb()
    {
        var converter = new GeographyConverter();
        var value = Geography.FromWellKnownText("POINT(-122.3 47.6)", 4269);

        var bytes = Assert.IsType<byte[]>(converter.ToProviderValue(value, SupportedDatabase.SqlServer));
        var server = SqlGeography.STGeomFromWKB(new SqlBytes(bytes[4..]), BinaryPrimitives.ReadInt32BigEndian(bytes));

        Assert.Equal(4269, server.STSrid.Value);
        Assert.Equal(47.6, server.Lat.Value, 10);
        Assert.Equal(-122.3, server.Long.Value, 10);
    }

    [Fact]
    public void Geometry_ReadWithTheLibraryLoaded_WritesItsWkbAndSrid()
    {
        var converter = new GeometryConverter();
        var sqlGeometry = SqlGeometry.STGeomFromText(new SqlChars("POINT(7 8)".ToCharArray()), 3857);
        Assert.True(converter.TryConvertFromProvider(sqlGeometry, SupportedDatabase.SqlServer, out var read));

        var server = ServerGeometry(converter.ToProviderValue(read, SupportedDatabase.SqlServer));

        Assert.Equal(3857, server.STSrid.Value);
        Assert.Equal("POINT (7 8)", server.STAsText().ToSqlString().Value);
    }

    [Fact]
    public void Geometry_GeoJsonOnly_ThrowsClearError()
    {
        var converter = new GeometryConverter();
        var value = Geometry.FromGeoJson("{\"type\":\"Point\",\"coordinates\":[1,2]}", 3857);

        var ex = Assert.ThrowsAny<Exception>(() => converter.ToProviderValue(value, SupportedDatabase.SqlServer));
        var root = ex.GetBaseException();
        Assert.IsType<NotSupportedException>(root);
        Assert.Contains("WKB or WKT", root.Message);
    }

    [Fact]
    public void Geometry_ReadFromSqlGeometry_KeepsSrid()
    {
        var converter = new GeometryConverter();
        var sqlGeometry = SqlGeometry.STGeomFromText(new SqlChars("POINT(5 6)".ToCharArray()), 3857);

        Assert.True(converter.TryConvertFromProvider(sqlGeometry, SupportedDatabase.SqlServer, out var result));
        Assert.Equal(3857, result.Srid);
        Assert.Equal(sqlGeometry.STAsBinary().Value, result.WellKnownBinary.ToArray());
    }

    [Fact]
    public void Geography_ReadFromSqlGeography_KeepsSrid()
    {
        var converter = new GeographyConverter();
        var sqlGeography = SqlGeography.STGeomFromText(new SqlChars("POINT(-122.3 47.6)".ToCharArray()), 4269);

        Assert.True(converter.TryConvertFromProvider(sqlGeography, SupportedDatabase.SqlServer, out var result));
        Assert.Equal(4269, result.Srid);
    }
}
