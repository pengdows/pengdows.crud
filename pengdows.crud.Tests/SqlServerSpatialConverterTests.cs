#region

using System;
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

    [Fact]
    public void Geometry_FromWkb_WritesSqlGeometryWithSrid()
    {
        var converter = new GeometryConverter();
        var value = Geometry.FromWellKnownBinary(Wkb("POINT(1 2)", 3857), 3857);

        var provider = Assert.IsType<SqlGeometry>(converter.ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(3857, provider.STSrid.Value);
        Assert.Equal("POINT (1 2)", provider.STAsText().ToSqlString().Value);
    }

    [Fact]
    public void Geometry_FromWkt_WritesSqlGeometryWithSrid()
    {
        var converter = new GeometryConverter();
        var value = Geometry.FromWellKnownText("POINT(3 4)", 3857);

        var provider = Assert.IsType<SqlGeometry>(converter.ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(3857, provider.STSrid.Value);
        Assert.Equal("POINT (3 4)", provider.STAsText().ToSqlString().Value);
    }

    [Fact]
    public void Geography_FromWkt_WritesSqlGeographyWithSrid()
    {
        var converter = new GeographyConverter();
        var value = Geography.FromWellKnownText("POINT(-122.3 47.6)", 4269);

        var provider = Assert.IsType<SqlGeography>(converter.ToProviderValue(value, SupportedDatabase.SqlServer));

        Assert.Equal(4269, provider.STSrid.Value);
    }

    [Fact]
    public void Geometry_GeoJsonOnly_ThrowsClearError()
    {
        var converter = new GeometryConverter();
        var value = Geometry.FromGeoJson("{\"type\":\"Point\",\"coordinates\":[1,2]}", 3857);

        var ex = Assert.ThrowsAny<Exception>(() => converter.ToProviderValue(value, SupportedDatabase.SqlServer));
        var root = ex.GetBaseException();
        Assert.IsType<InvalidOperationException>(root);
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
