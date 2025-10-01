using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedTypeConverterTests
{
    #region Network Type Tests

    [Fact]
    public void InetConverter_ShouldConvertFromString()
    {
        var converter = new InetConverter();
        var success = converter.TryConvertFromProvider("192.168.1.1/24", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), result.Address);
        Assert.Equal((byte?)24, result.PrefixLength);
    }

    [Fact]
    public void InetConverter_ShouldConvertFromIPAddress()
    {
        var converter = new InetConverter();
        var address = IPAddress.Parse("10.0.0.1");
        var success = converter.TryConvertFromProvider(address, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(address, result.Address);
        Assert.Null(result.PrefixLength);
    }

    [Fact]
    public void InetConverter_ShouldFailOnInvalidString()
    {
        var converter = new InetConverter();
        var success = converter.TryConvertFromProvider("invalid", SupportedDatabase.PostgreSql, out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void CidrConverter_ShouldConvertFromString()
    {
        var converter = new CidrConverter();
        var success = converter.TryConvertFromProvider("192.168.0.0/16", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(IPAddress.Parse("192.168.0.0"), result.Network);
        Assert.Equal((byte?)16, result.PrefixLength);
    }

    [Fact]
    public void CidrConverter_ShouldFailOnMissingPrefix()
    {
        var converter = new CidrConverter();
        var success = converter.TryConvertFromProvider("192.168.1.1", SupportedDatabase.PostgreSql, out var result);

        Assert.False(success);
    }

    [Fact]
    public void MacAddressConverter_ShouldConvertFromString()
    {
        var converter = new MacAddressConverter();
        var success = converter.TryConvertFromProvider("00:11:22:33:44:55", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.NotNull(result.Address);
    }

    [Fact]
    public void MacAddressConverter_ShouldConvertFromPhysicalAddress()
    {
        var converter = new MacAddressConverter();
        var physical = new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 });
        var success = converter.TryConvertFromProvider(physical, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(physical, result.Address);
    }

    [Fact]
    public void MacAddressConverter_ToProviderValue_FormatsText()
    {
        var converter = new MacAddressConverter();
        var physical = new PhysicalAddress(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF });
        var mac = new MacAddress(physical);

        var providerValue = converter.ToProviderValue(mac, SupportedDatabase.PostgreSql);

        Assert.Equal("AA:BB:CC:DD:EE:FF", providerValue);
    }

    #endregion

    #region Spatial Type Tests

    [Fact]
    public void GeometryConverter_ShouldConvertFromWKT()
    {
        var converter = new GeometryConverter();
        var wkt = "POINT(1 2)";
        var success = converter.TryConvertFromProvider(wkt, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(wkt, result.WellKnownText);
        Assert.Equal(0, result.Srid); // Default SRID
    }

    [Fact]
    public void GeometryConverter_ShouldConvertFromWKTWithSRID()
    {
        var converter = new GeometryConverter();
        var wkt = "SRID=4326;POINT(1 2)";
        var success = converter.TryConvertFromProvider(wkt, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal("POINT(1 2)", result.WellKnownText);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_ShouldConvertFromWKT()
    {
        var converter = new GeographyConverter();
        var wkt = "POINT(1 2)";
        var success = converter.TryConvertFromProvider(wkt, SupportedDatabase.SqlServer, out var result);

        Assert.True(success);
        Assert.Equal(wkt, result.WellKnownText);
        Assert.Equal(4326, result.Srid); // Geography defaults to WGS84
    }

    [Fact]
    public void GeometryConverter_ShouldConvertFromGeoJSON()
    {
        var converter = new GeometryConverter();
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[1,2]}";
        var success = converter.TryConvertFromProvider(geoJson, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(geoJson, result.GeoJson);
    }

    [Fact]
    public void GeometryConverter_ShouldExtractSridFromEwkb()
    {
        var converter = new GeometryConverter();
        var wkb = BuildPointEwkb(4326, 12.34, 56.78);

        var success = converter.TryConvertFromProvider(wkb, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(4326, result.Srid);
        Assert.True(result.WellKnownBinary.Span.SequenceEqual(wkb));
    }

    [Fact]
    public void GeographyConverter_ShouldExtractSridFromGeoJsonWithCrs()
    {
        var converter = new GeographyConverter();
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[1,2],\"srid\":3857}";
        var success = converter.TryConvertFromProvider(geoJson, SupportedDatabase.SqlServer, out var result);

        Assert.True(success);
        Assert.Equal(3857, result.Srid);
        Assert.Equal(geoJson, result.GeoJson);
    }

    [Fact]
    public void GeometryConverter_ShouldHandleReadOnlyMemory()
    {
        var converter = new GeometryConverter();
        var memory = new ReadOnlyMemory<byte>(BuildPointEwkb(1234, 1.23, 4.56));

        var success = converter.TryConvertFromProvider(memory, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.True(result.WellKnownBinary.Span.Length > 0);
    }

    [Fact]
    public void GeometryConverter_ShouldHandleArraySegment()
    {
        var converter = new GeometryConverter();
        var segment = new ArraySegment<byte>(BuildPointEwkb(0, 7.89, 1.23));

        var success = converter.TryConvertFromProvider(segment, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.True(result.WellKnownBinary.Span.Length > 0);
    }

    [Fact]
    public void GeometryConverter_ToProviderValue_MySqlUsesBinary()
    {
        var geometry = Geometry.FromWellKnownBinary(BuildPointEwkb(0, 1.23, 4.56), 0);
        var converter = new GeometryConverter();

        var providerValue = converter.ToProviderValue(geometry, SupportedDatabase.MySql);

        Assert.IsType<byte[]>(providerValue);
    }

    [Fact]
    public void GeometryConverter_ToProviderValue_MySqlWithWkt_ReturnsUtf8Bytes()
    {
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);
        var converter = new GeometryConverter();

        var providerValue = converter.ToProviderValue(geometry, SupportedDatabase.MySql) as byte[];

        Assert.NotNull(providerValue);
        Assert.Equal("POINT(1 2)", Encoding.UTF8.GetString(providerValue!));
    }

    [Fact]
    public void GeometryConverter_FromProviderSpecific_NpgsqlWrapped()
    {
        var converter = new GeometryConverter();
        var stub = new FakeNpgsqlGeometry(BuildPointEwkb(4326, 1, 2));

        var success = converter.TryConvertFromProvider(stub, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(4326, result.Srid);
        Assert.Same(stub, result.ProviderValue);
    }

    [Fact]
    public void GeometryConverter_ToProviderValue_PostgresUsesGeoJsonWhenAvailable()
    {
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[3,4]}";
        var geometry = Geometry.FromGeoJson(geoJson, 4326);
        var converter = new GeometryConverter();

        var providerValue = converter.ToProviderValue(geometry, SupportedDatabase.PostgreSql);

        Assert.Equal(geoJson, providerValue);
    }

    [Fact]
    public void GeographyConverter_ToProviderValue_SqlServerRequiresProviderSpecific()
    {
        var geography = Geography.FromWellKnownText("POINT(0 1)", 4326);
        var converter = new GeographyConverter();

        Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geography, SupportedDatabase.SqlServer));
    }

    [Fact]
    public void SpatialConverter_ShouldHandleInvalidWKT()
    {
        var converter = new GeometryConverter();
        var success = converter.TryConvertFromProvider("INVALID WKT", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal("INVALID WKT", result.WellKnownText);
        Assert.Equal(0, result.Srid);
    }

    #endregion

    #region Interval Type Tests

    [Fact]
    public void PostgreSqlIntervalConverter_ShouldConvertFromString()
    {
        var converter = new PostgreSqlIntervalConverter();
        var iso = "P1Y2M3DT4H5M6S";
        var success = converter.TryConvertFromProvider(iso, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(2, result.Months);
        Assert.Equal(3, result.Days);
        Assert.True(result.Microseconds > 0);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_ShouldConvertFromTimeSpan()
    {
        var converter = new PostgreSqlIntervalConverter();
        var timeSpan = TimeSpan.FromHours(2.5);
        var success = converter.TryConvertFromProvider(timeSpan, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void IntervalDaySecondConverter_ShouldConvertFromTimeSpan()
    {
        var converter = new IntervalDaySecondConverter();
        var timeSpan = TimeSpan.FromDays(1).Add(TimeSpan.FromHours(2));
        var success = converter.TryConvertFromProvider(timeSpan, SupportedDatabase.Oracle, out var result);

        Assert.True(success);
        Assert.Equal(1, result.Days);
        Assert.Equal(TimeSpan.FromHours(2), result.Time);
    }

    [Fact]
    public void IntervalDaySecondConverter_ShouldConvertFromIsoString()
    {
        var converter = new IntervalDaySecondConverter();
        var success = converter.TryConvertFromProvider("P3DT4H5M6S", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(3, result.Days);
        Assert.Equal(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(6), result.Time);
    }

    [Fact]
    public void IntervalDaySecondConverter_ToProviderValue_FormatsIso()
    {
        var converter = new IntervalDaySecondConverter();
        var value = new IntervalDaySecond(2, new TimeSpan(4, 5, 6));

        var formatted = converter.ToProviderValue(value, SupportedDatabase.PostgreSql);

        Assert.Equal("P2DT4H5M6S", formatted);
    }

    [Fact]
    public void IntervalYearMonthConverter_ShouldConvertFromString()
    {
        var converter = new IntervalYearMonthConverter();
        var iso = "P1Y6M";
        var success = converter.TryConvertFromProvider(iso, SupportedDatabase.Oracle, out var result);

        Assert.True(success);
        Assert.Equal(1, result.Years);
        Assert.Equal(6, result.Months);
    }

    [Fact]
    public void IntervalConverter_ShouldTreatInvalidStringAsZero()
    {
        var converter = new PostgreSqlIntervalConverter();
        var success = converter.TryConvertFromProvider("invalid", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(0, result.Months);
        Assert.Equal(0, result.Days);
        Assert.Equal(0, result.Microseconds);
    }

    #endregion

    #region Range Type Tests

    [Fact]
    public void PostgreSqlRangeConverter_ShouldConvertFromString()
    {
        var converter = new PostgreSqlRangeConverter<int>();
        var range = "[1,10)";
        var success = converter.TryConvertFromProvider(range, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(1, result.Lower);
        Assert.Equal(10, result.Upper);
        Assert.True(result.IsLowerInclusive);
        Assert.False(result.IsUpperInclusive);
    }

    [Fact]
    public void PostgreSqlRangeConverter_ShouldConvertFromTuple()
    {
        var converter = new PostgreSqlRangeConverter<int>();
        var tuple = new Tuple<int?, int?>(5, 15);
        var success = converter.TryConvertFromProvider(tuple, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(5, result.Lower);
        Assert.Equal(15, result.Upper);
    }

    [Fact]
    public void PostgreSqlRangeConverter_ShouldHandleEmptyRange()
    {
        var converter = new PostgreSqlRangeConverter<int>();
        var range = "(,)";
        var success = converter.TryConvertFromProvider(range, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.False(result.HasLowerBound);
        Assert.False(result.HasUpperBound);
    }

    [Fact]
    public void PostgreSqlRangeConverter_ToProviderValue_FormatsRange()
    {
        var converter = new PostgreSqlRangeConverter<int>();
        var range = new Range<int>(5, 10, isLowerInclusive: true, isUpperInclusive: false);

        var providerValue = converter.ToProviderValue(range, SupportedDatabase.PostgreSql);

        Assert.Equal("[5,10)", providerValue);
    }

    #endregion

    #region LOB Type Tests

    [Fact]
    public void BlobStreamConverter_ShouldConvertFromByteArray()
    {
        var converter = new BlobStreamConverter();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var success = converter.TryConvertFromProvider(bytes, SupportedDatabase.SqlServer, out var result);

        Assert.True(success);
        Assert.IsType<MemoryStream>(result);

        var buffer = new byte[5];
        result.Read(buffer, 0, 5);
        Assert.Equal(bytes, buffer);
    }

    [Fact]
    public void BlobStreamConverter_ShouldConvertFromStream()
    {
        var converter = new BlobStreamConverter();
        var originalStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var success = converter.TryConvertFromProvider(originalStream, SupportedDatabase.SqlServer, out var result);

        Assert.True(success);
        Assert.Same(originalStream, result);
        Assert.Equal(0, result.Position); // Should be reset to beginning
    }

    [Fact]
    public void ClobStreamConverter_ShouldConvertFromString()
    {
        var converter = new ClobStreamConverter();
        var text = "Hello, World!";
        var success = converter.TryConvertFromProvider(text, SupportedDatabase.Oracle, out var result);

        Assert.True(success);
        Assert.IsType<StringReader>(result);

        var readText = result.ReadToEnd();
        Assert.Equal(text, readText);
    }

    [Fact]
    public void ClobStreamConverter_ShouldConvertFromStream()
    {
        var converter = new ClobStreamConverter();
        var text = "Hello, Stream!";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var success = converter.TryConvertFromProvider(stream, SupportedDatabase.Oracle, out var result);

        Assert.True(success);
        Assert.IsType<StreamReader>(result);

        var readText = result.ReadToEnd();
        Assert.Equal(text, readText);
    }

    #endregion

    #region Concurrency Token Tests

    [Fact]
    public void RowVersionConverter_ShouldConvertFromByteArray()
    {
        var converter = new RowVersionConverter();
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        var success = converter.TryConvertFromProvider(bytes, SupportedDatabase.SqlServer, out var result);

        Assert.True(success);
        Assert.Equal(bytes, result.ToArray());
    }

    [Fact]
    public void RowVersionConverter_ShouldConvertFromMemory()
    {
        var converter = new RowVersionConverter();
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 };
        var memory = new ReadOnlyMemory<byte>(bytes);
        var success = converter.TryConvertFromProvider(memory, SupportedDatabase.SqlServer, out var result);

        Assert.True(success);
        Assert.Equal(bytes, result.ToArray());
    }

    [Fact]
    public void RowVersionConverter_ShouldFailOnInvalidInput()
    {
        var converter = new RowVersionConverter();
        var success = converter.TryConvertFromProvider("invalid", SupportedDatabase.SqlServer, out var result);

        Assert.False(success);
    }

    #endregion

    #region Provider Value Tests

    [Fact]
    public void InetConverter_ToProviderValue_PostgreSql_ShouldReturnString()
    {
        var converter = new InetConverter();
        var inet = new Inet(IPAddress.Parse("192.168.1.1"), 24);
        var result = converter.ToProviderValue(inet, SupportedDatabase.PostgreSql);

        Assert.IsType<string>(result);
        Assert.Equal("192.168.1.1/24", result);
    }

    [Fact]
    public void GeometryConverter_ToProviderValue_ShouldPreferProviderValue()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);
        var providerObj = new object();
        geometry = geometry.WithProviderValue(providerObj);

        var result = converter.ToProviderValue(geometry, SupportedDatabase.SqlServer);

        Assert.Same(providerObj, result);
    }

    [Fact]
    public void BlobStreamConverter_ToProviderValue_ShouldResetPosition()
    {
        var converter = new BlobStreamConverter();
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        stream.Seek(2, SeekOrigin.Begin); // Move position away from start

        var result = converter.ToProviderValue(stream, SupportedDatabase.SqlServer);

        Assert.Same(stream, result);
        Assert.Equal(0, stream.Position); // Should be reset to beginning
    }

    #endregion

    private static byte[] BuildPointEwkb(int srid, double x, double y)
    {
        var buffer = new byte[1 + 4 + 4 + 16];
        buffer[0] = 1;
        var type = 0x00000001u | 0x20000000u;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), type);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(5), srid);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer.AsSpan(9), x);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer.AsSpan(17), y);
        return buffer;
    }

    private sealed class FakeNpgsqlGeometry
    {
        public FakeNpgsqlGeometry(byte[] bytes) => AsBinary = bytes;
        public byte[] AsBinary { get; }
    }
}
