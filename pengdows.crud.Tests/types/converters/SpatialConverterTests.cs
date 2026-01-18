using System;
using System.Buffers;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.converters;

public static class SpatialConverterTests
{
    private static readonly GeometryConverter GeometryConverter = new();
    private static readonly GeographyConverter GeographyConverter = new();

    [Fact]
    public static void GeometryConverter_ExtractsSridFromEwkb()
    {
        var buffer = CreateEwkbPoint(srid: 3857, x: 1, y: 2);

        Assert.True(GeometryConverter.TryConvertFromProvider(buffer, SupportedDatabase.PostgreSql, out var geometry));
        Assert.Equal(3857, geometry.Srid);
        Assert.True(geometry.WellKnownBinary.Span.SequenceEqual(buffer));
    }

    [Fact]
    public static void GeometryConverter_ParseWktWithSrid()
    {
        var wkt = "SRID=4326;POINT(5 6)";
        Assert.True(GeometryConverter.TryConvertFromProvider(wkt, SupportedDatabase.PostgreSql, out var geometry));
        Assert.Equal(4326, geometry.Srid);
        Assert.Equal("POINT(5 6)", geometry.WellKnownText);
    }

    [Fact]
    public static void GeographyConverter_DefaultsTo4326WhenMissing()
    {
        const string wkt = "POINT(0 0)";
        Assert.True(GeographyConverter.TryConvertFromProvider(wkt, SupportedDatabase.PostgreSql, out var geography));
        Assert.Equal(4326, geography.Srid);
    }

    [Fact]
    public static void GeographyConverter_ParsesGeoJsonSrid()
    {
        const string geoJson = "{\"type\":\"Point\",\"srid\":9999,\"coordinates\":[0,0]}";
        Assert.True(GeographyConverter.TryConvertFromProvider(geoJson, SupportedDatabase.PostgreSql, out var geography));
        Assert.Equal(9999, geography.Srid);
    }

    [Fact]
    public static void SpatialConverter_ReturnsGeoJsonForPostgresWhenSupplied()
    {
        var geometry = Geometry.FromGeoJson("{\"type\":\"Point\"}", 3857);
        var providerValue = GeometryConverter.ToProviderValue(geometry, SupportedDatabase.PostgreSql);

        Assert.Equal("{\"type\":\"Point\"}", providerValue);
    }

    [Fact]
    public static void SpatialConverter_ThrowsForSqlServerWithoutProviderTypes()
    {
        var geometry = Geometry.FromWellKnownText("POINT(0 0)", 4326);
        var ex = Assert.Throws<InvalidOperationException>(() => GeometryConverter.ToProviderValue(geometry, SupportedDatabase.SqlServer));
        Assert.Contains("Microsoft.SqlServer.Types", ex.Message);
    }

    [Fact]
    public static void SpatialConverter_ReadsFromProviderSpecificShim()
    {
        var shim = new SqlGeometry
        {
            Wkb = CreateEwkbPoint(4326, 0, 0)
        };

        Assert.True(GeometryConverter.TryConvertFromProvider(shim, SupportedDatabase.SqlServer, out var fromProvider));
        Assert.Equal(4326, fromProvider.Srid);
    }

    [Fact]
    public static void SpatialConverter_ReadsFromReadOnlyMemory()
    {
        var memory = new ReadOnlyMemory<byte>(CreateEwkbPoint(4326, 1, 1));
        Assert.True(GeometryConverter.TryConvertFromProvider(memory, SupportedDatabase.PostgreSql, out var geometry));
        Assert.Equal(4326, geometry.Srid);
    }

    private static byte[] CreateEwkbPoint(int srid, double x, double y)
    {
        var buffer = new byte[1 + 4 + 4 + 16];
        buffer[0] = 1; // little endian
        var type = 0x20000000 | 1; // POINT with SRID flag
        BitConverter.TryWriteBytes(buffer.AsSpan(1), type);
        BitConverter.TryWriteBytes(buffer.AsSpan(5), srid);
        BitConverter.TryWriteBytes(buffer.AsSpan(9), BitConverter.DoubleToInt64Bits(x));
        BitConverter.TryWriteBytes(buffer.AsSpan(17), BitConverter.DoubleToInt64Bits(y));
        return buffer;
    }

    private sealed class SqlGeometry
    {
        public byte[] Wkb { get; init; } = Array.Empty<byte>();
        public int STSrid => 4326;

        public byte[] STAsBinary() => Wkb;
    }
}
