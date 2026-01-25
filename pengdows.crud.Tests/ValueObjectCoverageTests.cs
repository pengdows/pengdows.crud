using System;
using System.Text;
using Xunit;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests to improve coverage of value object types.
/// Targets uncovered lines in SpatialValue, Geometry, Geography, and related types.
/// </summary>
public class ValueObjectCoverageTests
{
    #region SpatialValue Tests (via Geometry subclass)

    [Fact]
    public void Geometry_FromWellKnownBinary_WithNegativeSrid_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Geometry.FromWellKnownBinary(wkb, -1));

        Assert.Contains("SRID must be non-negative", ex.Message);
        Assert.Equal("srid", ex.ParamName);
    }

    [Fact]
    public void Geometry_Equals_WithNull_ReturnsFalse()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 4326);

        // Act
        var result = geometry.Equals(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Geometry_Equals_WithSameReference_ReturnsTrue()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 4326);

        // Act
        var result = geometry.Equals(geometry);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Geometry_Equals_WithEqualValues_ReturnsTrue()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry1 = Geometry.FromWellKnownBinary(wkb, 4326);
        var geometry2 = Geometry.FromWellKnownBinary(wkb, 4326);

        // Act
        var result = geometry1.Equals(geometry2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Geometry_Equals_WithDifferentSrid_ReturnsFalse()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry1 = Geometry.FromWellKnownBinary(wkb, 4326);
        var geometry2 = Geometry.FromWellKnownBinary(wkb, 3857);

        // Act
        var result = geometry1.Equals(geometry2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Geometry_Equals_WithDifferentFormat_ReturnsFalse()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry1 = Geometry.FromWellKnownBinary(wkb, 4326);
        var geometry2 = Geometry.FromWellKnownText("POINT(1 2)", 4326);

        // Act
        var result = geometry1.Equals(geometry2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Geometry_Equals_WithDifferentWellKnownText_ReturnsFalse()
    {
        // Arrange
        var geometry1 = Geometry.FromWellKnownText("POINT(1 2)", 4326);
        var geometry2 = Geometry.FromWellKnownText("POINT(3 4)", 4326);

        // Act
        var result = geometry1.Equals(geometry2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Geometry_Equals_WithDifferentGeoJson_ReturnsFalse()
    {
        // Arrange
        var geometry1 = Geometry.FromGeoJson("{\"type\":\"Point\",\"coordinates\":[1,2]}", 4326);
        var geometry2 = Geometry.FromGeoJson("{\"type\":\"Point\",\"coordinates\":[3,4]}", 4326);

        // Act
        var result = geometry1.Equals(geometry2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Geometry_Equals_WithDifferentBinary_ReturnsFalse()
    {
        // Arrange
        var wkb1 = new byte[] { 1, 2, 3, 4 };
        var wkb2 = new byte[] { 5, 6, 7, 8 };
        var geometry1 = Geometry.FromWellKnownBinary(wkb1, 4326);
        var geometry2 = Geometry.FromWellKnownBinary(wkb2, 4326);

        // Act
        var result = geometry1.Equals(geometry2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Geometry_EqualsObject_WithNonSpatialValue_ReturnsFalse()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 4326);
        object other = "not a spatial value";

        // Act
        var result = geometry.Equals(other);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Geometry_GetHashCode_ComputesCorrectly()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 4326);

        // Act
        var hash1 = geometry.GetHashCode();
        var hash2 = geometry.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2); // Same object should have same hash
    }

    [Fact]
    public void Geometry_GetHashCode_WithEmptyBinary_ComputesCorrectly()
    {
        // Arrange
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 4326);

        // Act
        var hash = geometry.GetHashCode();

        // Assert
        Assert.NotEqual(0, hash); // Should still compute a hash
    }

    [Fact]
    public void Geometry_ToString_WithWellKnownText_ReturnsText()
    {
        // Arrange
        var wkt = "POINT(1 2)";
        var geometry = Geometry.FromWellKnownText(wkt, 4326);

        // Act
        var result = geometry.ToString();

        // Assert
        Assert.Equal(wkt, result);
    }

    [Fact]
    public void Geometry_ToString_WithGeoJson_ReturnsJson()
    {
        // Arrange
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[1,2]}";
        var geometry = Geometry.FromGeoJson(geoJson, 4326);

        // Act
        var result = geometry.ToString();

        // Assert
        Assert.Equal(geoJson, result);
    }

    [Fact]
    public void Geometry_ToString_WithWellKnownBinary_ReturnsBase64()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 4326);

        // Act
        var result = geometry.ToString();

        // Assert
        Assert.Equal(Convert.ToBase64String(wkb), result);
    }

    #endregion

    #region Geography Tests

    [Fact]
    public void Geography_FromWellKnownBinary_WithNegativeSrid_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Geography.FromWellKnownBinary(wkb, -1));

        Assert.Contains("SRID must be non-negative", ex.Message);
    }

    [Fact]
    public void Geography_Equals_WorksCorrectly()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4 };
        var geography1 = Geography.FromWellKnownBinary(wkb, 4326);
        var geography2 = Geography.FromWellKnownBinary(wkb, 4326);

        // Act
        var result = geography1.Equals(geography2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Geography_GetHashCode_WorksCorrectly()
    {
        // Arrange
        var wkb = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var geography = Geography.FromWellKnownBinary(wkb, 4326);

        // Act
        var hash = geography.GetHashCode();

        // Assert - Hash should incorporate binary data
        Assert.NotEqual(0, hash);
    }

    #endregion

    #region JsonValue Tests

    [Fact]
    public void JsonValue_Equals_WithDefaultValue_ReturnsFalse()
    {
        // Arrange
        var json = new JsonValue("{\"test\":true}");
        var defaultValue = default(JsonValue);

        // Act
        var result = json.Equals(defaultValue);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void JsonValue_Equals_WithSameReference_ReturnsTrue()
    {
        // Arrange
        var json = new JsonValue("{\"test\":true}");

        // Act
        var result = json.Equals(json);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void JsonValue_Equals_WithEqualValue_ReturnsTrue()
    {
        // Arrange
        var json1 = new JsonValue("{\"test\":true}");
        var json2 = new JsonValue("{\"test\":true}");

        // Act
        var result = json1.Equals(json2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void JsonValue_Equals_WithDifferentValue_ReturnsFalse()
    {
        // Arrange
        var json1 = new JsonValue("{\"test\":true}");
        var json2 = new JsonValue("{\"test\":false}");

        // Act
        var result = json1.Equals(json2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void JsonValue_EqualsObject_WithNonJsonValue_ReturnsFalse()
    {
        // Arrange
        var json = new JsonValue("{\"test\":true}");
        object other = "not a json value";

        // Act
        var result = json.Equals(other);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void JsonValue_GetHashCode_IsConsistent()
    {
        // Arrange
        var json = new JsonValue("{\"test\":true}");

        // Act
        var hash1 = json.GetHashCode();
        var hash2 = json.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void JsonValue_ToString_ReturnsRawJson()
    {
        // Arrange
        var rawJson = "{\"test\":true,\"value\":42}";
        var json = new JsonValue(rawJson);

        // Act
        var result = json.ToString();

        // Assert
        Assert.Equal(rawJson, result);
    }

    #endregion
}
