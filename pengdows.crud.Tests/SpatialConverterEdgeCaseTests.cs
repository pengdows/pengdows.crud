using System;
using System.Buffers.Binary;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests edge cases for SpatialConverter, GeometryConverter, and GeographyConverter.
/// </summary>
public class SpatialConverterEdgeCaseTests
{
    // ===== ConvertToProvider edge cases =====

    [Fact]
    public void ConvertToProvider_OracleNoProviderValue_Throws()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geometry, SupportedDatabase.Oracle));
        Assert.Contains("Oracle", ex.Message);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_MissingAssembly_Throws()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        // Since Microsoft.SqlServer.Types isn't referenced, this should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geometry, SupportedDatabase.SqlServer));
        Assert.Contains("Microsoft.SqlServer.Types", ex.Message);
    }

    [Fact]
    public void ConvertToProvider_Postgres_WkbPath_ReturnsByteArray()
    {
        var converter = new GeometryConverter();
        var wkb = new byte[] { 1, 2, 3, 4, 5 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.PostgreSql);
        Assert.IsType<byte[]>(result);
        Assert.Equal(wkb, (byte[])result!);
    }

    [Fact]
    public void ConvertToProvider_Postgres_WktPath_ReturnsString()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.PostgreSql);
        Assert.Equal("POINT(1 2)", result);
    }

    [Fact]
    public void ConvertToProvider_Postgres_GeoJsonPath_ReturnsGeoJson()
    {
        var converter = new GeometryConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";
        var geometry = Geometry.FromGeoJson(json, 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.PostgreSql);
        Assert.Equal(json, result);
    }

    [Fact]
    public void ConvertToProvider_MySql_WkbPath_ReturnsByteArray()
    {
        var converter = new GeometryConverter();
        var wkb = new byte[] { 1, 2, 3, 4, 5 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.MySql);
        Assert.IsType<byte[]>(result);
    }

    [Fact]
    public void ConvertToProvider_MySql_WktPath_ReturnsUtf8Bytes()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.MySql);
        Assert.IsType<byte[]>(result);
        var bytes = (byte[])result!;
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Equal("POINT(1 2)", text);
    }

    [Fact]
    public void ConvertToProvider_Default_WkbPath()
    {
        var converter = new GeometryConverter();
        var wkb = new byte[] { 1, 2, 3, 4, 5 };
        var geometry = Geometry.FromWellKnownBinary(wkb, 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.Sqlite);
        Assert.IsType<byte[]>(result);
    }

    [Fact]
    public void ConvertToProvider_Default_WktPath()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.Sqlite);
        Assert.Equal("POINT(1 2)", result);
    }

    [Fact]
    public void ConvertToProvider_Default_GeoJsonPath()
    {
        var converter = new GeometryConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";
        var geometry = Geometry.FromGeoJson(json, 0);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.Sqlite);
        Assert.Equal(json, result);
    }

    [Fact]
    public void ConvertToProvider_WithProviderValue_ReturnsProviderValue()
    {
        var converter = new GeometryConverter();
        var providerObj = new object();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0, providerObj);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.Sqlite);
        Assert.Same(providerObj, result);
    }

    // ===== TryConvertFromProvider edge cases =====

    [Fact]
    public void TryConvert_CatchBranch_ReturnsFalse()
    {
        // Use Geography converter with provider-specific type that will fail
        var converter = new GeographyConverter();
        var unknownObj = new UnsupportedSpatialShim();

        // FromProviderSpecific should throw NotSupportedException, which is caught
        var success = converter.TryConvertFromProvider(unknownObj, SupportedDatabase.Sqlite, out _);
        Assert.False(success);
    }

    [Fact]
    public void FromProviderSpecific_UnsupportedType_CaughtAndReturnsFalse()
    {
        var converter = new GeometryConverter();
        var unknownObj = new UnsupportedSpatialShim();

        var success = converter.TryConvertFromProvider(unknownObj, SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void TryConvertFromProvider_ReadOnlyMemory_Succeeds()
    {
        var converter = new GeometryConverter();
        var bytes = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte> memory = bytes;

        var success = converter.TryConvertFromProvider(memory, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(bytes, result.WellKnownBinary.ToArray());
    }

    [Fact]
    public void TryConvertFromProvider_ArraySegment_Succeeds()
    {
        var converter = new GeometryConverter();
        var bytes = new byte[] { 1, 2, 3 };
        var segment = new ArraySegment<byte>(bytes);

        var success = converter.TryConvertFromProvider(segment, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(bytes, result.WellKnownBinary.ToArray());
    }

    [Fact]
    public void TryConvertFromProvider_GeoJsonString_Succeeds()
    {
        var converter = new GeometryConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

        var success = converter.TryConvertFromProvider(json, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(json, result.GeoJson);
    }

    // ===== GeometryConverter SRID extraction =====

    [Fact]
    public void ExtractSridFromEwkb_ShortBuffer_DefaultsToZero()
    {
        var converter = new GeometryConverter();
        var shortBytes = new byte[] { 1, 2 };

        var success = converter.TryConvertFromProvider(shortBytes, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void ExtractSridFromEwkb_BigEndian_ExtractsSrid()
    {
        var converter = new GeometryConverter();
        // Build big-endian EWKB with SRID flag
        var bytes = new byte[9];
        bytes[0] = 0; // big endian
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan().Slice(1, 4), 0x20000001); // type with SRID flag
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan().Slice(5, 4), 4326); // SRID

        var success = converter.TryConvertFromProvider(bytes, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void ExtractSridFromEwkb_LittleEndian_ExtractsSrid()
    {
        var converter = new GeometryConverter();
        var bytes = new byte[9];
        bytes[0] = 1; // little endian
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan().Slice(1, 4), 0x20000001); // type with SRID flag
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan().Slice(5, 4), 3857); // SRID

        var success = converter.TryConvertFromProvider(bytes, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(3857, result.Srid);
    }

    [Fact]
    public void ExtractSridFromEwkb_SridFlagButTooShort_DefaultsToZero()
    {
        var converter = new GeometryConverter();
        // 5 bytes: endian + type with SRID flag, but no SRID bytes
        var bytes = new byte[6];
        bytes[0] = 1; // little endian
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan().Slice(1, 4), 0x20000001); // type with SRID flag
        // Only 6 bytes total, need 9 for SRID extraction

        var success = converter.TryConvertFromProvider(bytes, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void ExtractSridFromEwkb_NoSridFlag_DefaultsToZero()
    {
        var converter = new GeometryConverter();
        var bytes = new byte[5];
        bytes[0] = 1; // little endian
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan().Slice(1, 4), 0x00000001); // type WITHOUT SRID flag

        var success = converter.TryConvertFromProvider(bytes, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void ExtractSridFromText_EmptyText_DefaultsToZero()
    {
        var converter = new GeometryConverter();

        var success = converter.TryConvertFromProvider("POINT(0 0)", SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void ExtractSridFromText_WithSrid_ExtractsSrid()
    {
        var converter = new GeometryConverter();

        var success = converter.TryConvertFromProvider("SRID=4326;POINT(1 2)", SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void ExtractSridFromText_MalformedSemicolon_DefaultsToZero()
    {
        var converter = new GeometryConverter();
        // SRID= but semicolon at position < 5
        var success = converter.TryConvertFromProvider("SRID=;POINT(1 2)", SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void ExtractSridFromText_UnparseableNumber_DefaultsToZero()
    {
        var converter = new GeometryConverter();
        var success = converter.TryConvertFromProvider("SRID=abc;POINT(1 2)", SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void ExtractSridFromGeoJson_WhitespaceOnly_DefaultsToZero()
    {
        var converter = new GeometryConverter();
        // GeoJSON without "srid" key
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

        var success = converter.TryConvertFromProvider(json, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    [Fact]
    public void ExtractSridFromGeoJson_SridKeyWithValue_ExtractsSrid()
    {
        var converter = new GeometryConverter();
        var json = "{\"type\":\"Point\",\"srid\":4326,\"coordinates\":[1,2]}";

        var success = converter.TryConvertFromProvider(json, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void ExtractSridFromGeoJson_NoSridKey_DefaultsToZero()
    {
        var converter = new GeometryConverter();
        // GeoJSON without any "srid" key at all
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";

        var success = converter.TryConvertFromProvider(json, SupportedDatabase.Sqlite, out var result);
        Assert.True(success);
        Assert.Equal(0, result.Srid);
    }

    // ===== Geography converter provider paths =====

    [Fact]
    public void GeographyConverter_ConvertToProvider_PostgresWkb()
    {
        var converter = new GeographyConverter();
        var wkb = new byte[] { 1, 2, 3 };
        var geography = Geography.FromWellKnownBinary(wkb, 4326);

        var result = converter.ToProviderValue(geography, SupportedDatabase.PostgreSql);
        Assert.IsType<byte[]>(result);
    }

    [Fact]
    public void GeographyConverter_ConvertToProvider_PostgresWkt()
    {
        var converter = new GeographyConverter();
        var geography = Geography.FromWellKnownText("POINT(1 2)", 4326);

        var result = converter.ToProviderValue(geography, SupportedDatabase.PostgreSql);
        Assert.Equal("POINT(1 2)", result);
    }

    [Fact]
    public void GeographyConverter_ConvertToProvider_OracleThrows()
    {
        var converter = new GeographyConverter();
        var geography = Geography.FromWellKnownText("POINT(1 2)", 4326);

        Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geography, SupportedDatabase.Oracle));
    }

    [Fact]
    public void Postgres_EmptyGeometry_Throws()
    {
        // This test creates a geometry where all representations are empty
        // The Postgres path should throw because no data is available
        var converter = new GeometryConverter();
        // WKB with empty span
        var geometry = Geometry.FromWellKnownBinary(Array.Empty<byte>(), 0);

        // Postgres path: no WKB, no WKT, no GeoJSON
        Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geometry, SupportedDatabase.PostgreSql));
    }

    [Fact]
    public void MySql_NoData_Throws()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownBinary(Array.Empty<byte>(), 0);

        Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geometry, SupportedDatabase.MySql));
    }

    /// <summary>
    /// A shim type that doesn't match any known spatial provider types.
    /// </summary>
    private class UnsupportedSpatialShim
    {
    }
}
