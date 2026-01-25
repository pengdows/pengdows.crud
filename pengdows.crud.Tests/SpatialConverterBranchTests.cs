using System;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class SpatialConverterBranchTests
{
    private sealed class TestSpatialConverter : SpatialConverter<Geometry>
    {
        protected override Geometry FromBinary(ReadOnlySpan<byte> wkb, SupportedDatabase provider)
        {
            return Geometry.FromWellKnownBinary(wkb, 4326);
        }

        protected override Geometry FromTextInternal(string text, SupportedDatabase provider)
        {
            return Geometry.FromWellKnownText(text, 4326);
        }

        protected override Geometry FromGeoJsonInternal(string json, SupportedDatabase provider)
        {
            return Geometry.FromGeoJson(json, 4326);
        }

        protected override Geometry WrapWithProvider(Geometry spatial, object providerValue)
        {
            return spatial.WithProviderValue(providerValue);
        }
    }

    private sealed class SqlGeometryStub
    {
        public int STSrid { get; } = 4326;

        public byte[] STAsBinary()
        {
            return new byte[] { 1, 2, 3 };
        }
    }

    private sealed class NpgsqlGeometryStub
    {
        public byte[] AsBinary { get; } = new byte[] { 4, 5, 6 };
    }

    [Fact]
    public void ConvertToProvider_ReturnsProviderValueWhenPresent()
    {
        var converter = new TestSpatialConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 4326).WithProviderValue("provider");

        var result = converter.ToProviderValue(geometry, SupportedDatabase.PostgreSql);

        Assert.Equal("provider", result);
    }

    [Fact]
    public void ConvertToProvider_PostgresAndMySqlPaths()
    {
        var converter = new TestSpatialConverter();
        var wkb = Geometry.FromWellKnownBinary(new byte[] { 1 }, 4326);
        var wkt = Geometry.FromWellKnownText("POINT(1 2)", 4326);
        var geo = Geometry.FromGeoJson("{\"type\":\"Point\"}", 4326);

        Assert.IsType<byte[]>(converter.ToProviderValue(wkb, SupportedDatabase.PostgreSql));
        Assert.Equal("POINT(1 2)", converter.ToProviderValue(wkt, SupportedDatabase.PostgreSql));
        Assert.Equal("{\"type\":\"Point\"}", converter.ToProviderValue(geo, SupportedDatabase.PostgreSql));

        var mysqlBytes = converter.ToProviderValue(wkt, SupportedDatabase.MySql);
        Assert.IsType<byte[]>(mysqlBytes);

        Assert.Throws<InvalidOperationException>(() => converter.ToProviderValue(geo, SupportedDatabase.MySql));
    }

    [Fact]
    public void ConvertToProvider_DefaultAndOraclePaths()
    {
        var converter = new TestSpatialConverter();
        var geo = Geometry.FromGeoJson("{\"type\":\"Point\"}", 4326);

        Assert.Equal("{\"type\":\"Point\"}", converter.ToProviderValue(geo, SupportedDatabase.Unknown));
        Assert.Throws<InvalidOperationException>(() => converter.ToProviderValue(geo, SupportedDatabase.Oracle));
    }

    [Fact]
    public void TryConvertFromProvider_HandlesBinaryAndText()
    {
        var converter = new TestSpatialConverter();

        Assert.True(converter.TryConvertFromProvider(new byte[] { 1 }, SupportedDatabase.PostgreSql,
            out var fromBytes));
        Assert.Equal(SpatialFormat.WellKnownBinary, fromBytes.Format);

        Assert.True(converter.TryConvertFromProvider(new ReadOnlyMemory<byte>(new byte[] { 2 }),
            SupportedDatabase.PostgreSql, out var fromMemory));
        Assert.Equal(SpatialFormat.WellKnownBinary, fromMemory.Format);

        Assert.True(converter.TryConvertFromProvider(new ArraySegment<byte>(new byte[] { 3 }),
            SupportedDatabase.PostgreSql, out var fromSegment));
        Assert.Equal(SpatialFormat.WellKnownBinary, fromSegment.Format);

        Assert.True(converter.TryConvertFromProvider("POINT(1 2)", SupportedDatabase.PostgreSql, out var fromText));
        Assert.Equal(SpatialFormat.WellKnownText, fromText.Format);

        Assert.True(converter.TryConvertFromProvider("{\"type\":\"Point\"}", SupportedDatabase.PostgreSql,
            out var fromJson));
        Assert.Equal(SpatialFormat.GeoJson, fromJson.Format);
    }

    [Fact]
    public void TryConvertFromProvider_HandlesProviderSpecificTypes()
    {
        var converter = new TestSpatialConverter();
        var sql = new SqlGeometryStub();
        var npgsql = new NpgsqlGeometryStub();

        Assert.True(converter.TryConvertFromProvider(sql, SupportedDatabase.SqlServer, out var sqlSpatial));
        Assert.Same(sql, sqlSpatial.ProviderValue);

        Assert.True(converter.TryConvertFromProvider(npgsql, SupportedDatabase.PostgreSql, out var pgSpatial));
        Assert.Same(npgsql, pgSpatial.ProviderValue);
    }

    [Fact]
    public void TryConvertFromProvider_Unsupported_ReturnsFalse()
    {
        var converter = new TestSpatialConverter();
        Assert.False(converter.TryConvertFromProvider(new object(), SupportedDatabase.SqlServer, out _));
    }
}