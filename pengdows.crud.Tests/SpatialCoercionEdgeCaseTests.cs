using System;
using System.Data;
using Moq;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests edge cases for GeographyCoercion and GeometryCoercion.
/// </summary>
public class SpatialCoercionEdgeCaseTests
{
    // ===== GeographyCoercion TryRead =====

    [Fact]
    public void GeographyCoercion_TryRead_GeographyPassthrough()
    {
        var coercion = new GeographyCoercion();
        var geography = Geography.FromWellKnownText("POINT(1 2)", 4326);

        Assert.True(coercion.TryRead(new DbValue(geography), out var result));
        Assert.Same(geography, result);
    }

    [Fact]
    public void GeographyCoercion_TryRead_ByteArray_ReturnsGeography()
    {
        var coercion = new GeographyCoercion();
        var bytes = new byte[] { 1, 2, 3 };

        Assert.True(coercion.TryRead(new DbValue(bytes), out var result));
        Assert.Equal(bytes, result.WellKnownBinary.ToArray());
    }

    [Fact]
    public void GeographyCoercion_TryRead_GeoJsonString()
    {
        var coercion = new GeographyCoercion();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

        Assert.True(coercion.TryRead(new DbValue(json, typeof(string)), out var result));
        Assert.Equal(json, result.GeoJson);
    }

    [Fact]
    public void GeographyCoercion_TryRead_WktString()
    {
        var coercion = new GeographyCoercion();

        Assert.True(coercion.TryRead(new DbValue("POINT(1 2)", typeof(string)), out var result));
        Assert.Equal("POINT(1 2)", result.WellKnownText);
    }

    [Fact]
    public void GeographyCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new GeographyCoercion();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void GeographyCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new GeographyCoercion();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    // ===== GeographyCoercion TryWrite =====

    [Fact]
    public void GeographyCoercion_TryWrite_NullValue_SetsDBNull()
    {
        var coercion = new GeographyCoercion();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(null, param.Object));
        param.VerifySet(p => p.Value = DBNull.Value, Times.Once);
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);
    }

    [Fact]
    public void GeographyCoercion_TryWrite_WkbValue_SetsBinary()
    {
        var coercion = new GeographyCoercion();
        var geography = Geography.FromWellKnownBinary(new byte[] { 1, 2, 3 }, 4326);
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(geography, param.Object));
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);
    }

    [Fact]
    public void GeographyCoercion_TryWrite_WktOnly_SetsString()
    {
        var coercion = new GeographyCoercion();
        var geography = Geography.FromWellKnownText("POINT(1 2)", 4326);
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(geography, param.Object));
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    // ===== GeometryCoercion TryRead =====

    [Fact]
    public void GeometryCoercion_TryRead_GeometryPassthrough()
    {
        var coercion = new GeometryCoercion();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        Assert.True(coercion.TryRead(new DbValue(geometry), out var result));
        Assert.Same(geometry, result);
    }

    [Fact]
    public void GeometryCoercion_TryRead_GeoJsonString()
    {
        var coercion = new GeometryCoercion();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

        Assert.True(coercion.TryRead(new DbValue(json, typeof(string)), out var result));
        Assert.Equal(json, result.GeoJson);
    }

    [Fact]
    public void GeometryCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new GeometryCoercion();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void GeometryCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new GeometryCoercion();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    // ===== GeometryCoercion TryWrite =====

    [Fact]
    public void GeometryCoercion_TryWrite_NullValue_SetsDBNull()
    {
        var coercion = new GeometryCoercion();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(null, param.Object));
        param.VerifySet(p => p.Value = DBNull.Value, Times.Once);
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);
    }

    [Fact]
    public void GeometryCoercion_TryWrite_WkbValue_SetsBinary()
    {
        var coercion = new GeometryCoercion();
        var geometry = Geometry.FromWellKnownBinary(new byte[] { 1, 2, 3 }, 0);
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(geometry, param.Object));
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);
    }

    [Fact]
    public void GeometryCoercion_TryWrite_WktOnly_SetsString()
    {
        var coercion = new GeometryCoercion();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(geometry, param.Object));
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }
}
