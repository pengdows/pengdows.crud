using System;
using System.Data;
using Moq;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests edge cases for ConverterRead<Geography> and ConverterRead<Geometry>.
/// </summary>
public class SpatialCoercionEdgeCaseTests
{
    // ===== ConverterRead<Geography> TryRead =====

    [Fact]
    public void GeographyCoercion_TryRead_GeographyPassthrough()
    {
        var coercion = new ConverterRead<Geography>();
        var geography = Geography.FromWellKnownText("POINT(1 2)", 4326);

        Assert.True(coercion.TryRead(new DbValue(geography), out var result));
        Assert.Same(geography, result);
    }

    [Fact]
    public void GeographyCoercion_TryRead_ByteArray_ReturnsGeography()
    {
        var coercion = new ConverterRead<Geography>();
        var bytes = new byte[] { 1, 2, 3 };

        Assert.True(coercion.TryRead(new DbValue(bytes), out var result));
        Assert.Equal(bytes, result.WellKnownBinary.ToArray());
    }

    [Fact]
    public void GeographyCoercion_TryRead_GeoJsonString()
    {
        var coercion = new ConverterRead<Geography>();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

        Assert.True(coercion.TryRead(new DbValue(json, typeof(string)), out var result));
        Assert.Equal(json, result.GeoJson);
    }

    [Fact]
    public void GeographyCoercion_TryRead_WktString()
    {
        var coercion = new ConverterRead<Geography>();

        Assert.True(coercion.TryRead(new DbValue("POINT(1 2)", typeof(string)), out var result));
        Assert.Equal("POINT(1 2)", result.WellKnownText);
    }

    [Fact]
    public void GeographyCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ConverterRead<Geography>();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void GeographyCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new ConverterRead<Geography>();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    // ===== ConverterRead<Geography> TryWrite =====

    // ===== ConverterRead<Geometry> TryRead =====

    [Fact]
    public void GeometryCoercion_TryRead_GeometryPassthrough()
    {
        var coercion = new ConverterRead<Geometry>();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        Assert.True(coercion.TryRead(new DbValue(geometry), out var result));
        Assert.Same(geometry, result);
    }

    [Fact]
    public void GeometryCoercion_TryRead_GeoJsonString()
    {
        var coercion = new ConverterRead<Geometry>();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

        Assert.True(coercion.TryRead(new DbValue(json, typeof(string)), out var result));
        Assert.Equal(json, result.GeoJson);
    }

    [Fact]
    public void GeometryCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ConverterRead<Geometry>();

        Assert.False(coercion.TryRead(new DbValue(42), out _));
    }

    [Fact]
    public void GeometryCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new ConverterRead<Geometry>();

        Assert.False(coercion.TryRead(new DbValue(null), out _));
    }

    // ===== ConverterRead<Geometry> TryWrite =====

}
