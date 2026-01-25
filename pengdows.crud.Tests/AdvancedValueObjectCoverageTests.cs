using System;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedValueObjectCoverageTests
{
    [Fact]
    public void GeometryAndGeography_FromGeoJson_AndWithProviderValue()
    {
        var geoJson = "{\"type\":\"Point\"}";
        var provider = new object();

        var geometry = Geometry.FromGeoJson(geoJson, 0).WithProviderValue(provider);
        Assert.Equal(geoJson, geometry.GeoJson);
        Assert.Same(provider, geometry.ProviderValue);

        var geography = Geography.FromGeoJson(geoJson, 4326).WithProviderValue(provider);
        Assert.Equal(geoJson, geography.GeoJson);
        Assert.Same(provider, geography.ProviderValue);
    }

    [Fact]
    public void DbValue_ReportsNull_AndCasts()
    {
        var value = new DbValue("text", typeof(string));
        Assert.False(value.IsNull);
        Assert.Equal("text", value.As<string>());

        var nullValue = new DbValue(null);
        Assert.True(nullValue.IsNull);
        Assert.Null(nullValue.As<string>());
    }
}