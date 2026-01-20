using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedConvertersCoverageTests
{
    [Fact]
    public void AdvancedTypeConverter_ThrowsOnWrongType()
    {
        var converter = new InetConverter();
        var ex = Assert.Throws<ArgumentException>(
            () => converter.ToProviderValue("not-inet", SupportedDatabase.PostgreSql));

        Assert.Contains(typeof(Inet).FullName ?? "Inet", ex.Message);
    }

    [Fact]
    public void InetConverter_RoundTrips_StringsAndAddresses()
    {
        var converter = new InetConverter();
        var inet = Inet.Parse("10.0.0.1/24");

        var providerValue = converter.ToProviderValue(inet, SupportedDatabase.PostgreSql);
        Assert.Equal("10.0.0.1/24", providerValue);

        var parsed = (Inet?)converter.FromProviderValue("10.0.0.1/24", SupportedDatabase.PostgreSql);
        Assert.NotNull(parsed);
        Assert.Equal(inet.ToString(), parsed!.ToString());

        var fromIp = (Inet?)converter.FromProviderValue(IPAddress.Parse("10.0.0.2"), SupportedDatabase.Unknown);
        Assert.NotNull(fromIp);
        Assert.Equal("10.0.0.2", fromIp!.ToString());
    }

    [Fact]
    public void CidrConverter_FormatsForPostgres_AndParsesStrings()
    {
        var converter = new CidrConverter();
        var cidr = Cidr.Parse("192.168.0.0/16");

        var providerValue = converter.ToProviderValue(cidr, SupportedDatabase.PostgreSql);
        Assert.Equal("192.168.0.0/16", providerValue);

        var parsed = (Cidr?)converter.FromProviderValue("192.168.0.0/16", SupportedDatabase.PostgreSql);
        Assert.NotNull(parsed);
        Assert.Equal(cidr.ToString(), parsed!.ToString());
    }

    [Fact]
    public void MacAddressConverter_FormatsForPostgres_AndReadsPhysicalAddress()
    {
        var converter = new MacAddressConverter();
        var mac = MacAddress.Parse("00:11:22:33:44:55");

        var providerValue = converter.ToProviderValue(mac, SupportedDatabase.PostgreSql);
        Assert.Equal("00:11:22:33:44:55", providerValue);

        var physical = new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 });
        var fromPhysical = (MacAddress?)converter.FromProviderValue(physical, SupportedDatabase.Unknown);
        Assert.NotNull(fromPhysical);
        Assert.Equal(mac.ToString(), fromPhysical!.ToString());
    }

    [Fact]
    public void IntervalYearMonthConverter_FormatsIso_ForOracle()
    {
        var converter = new IntervalYearMonthConverter();
        var interval = new IntervalYearMonth(2, 3);

        var providerValue = converter.ToProviderValue(interval, SupportedDatabase.Oracle);
        Assert.Equal("P2Y3M", providerValue);

        var parsed = (IntervalYearMonth?)converter.FromProviderValue("P2Y3M", SupportedDatabase.Oracle);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Value.Years);
        Assert.Equal(3, parsed.Value.Months);
    }

    [Fact]
    public void IntervalDaySecondConverter_FormatsIso_ForOracle()
    {
        var converter = new IntervalDaySecondConverter();
        var interval = new IntervalDaySecond(1, new TimeSpan(2, 3, 4));

        var providerValue = converter.ToProviderValue(interval, SupportedDatabase.Oracle);
        Assert.Equal("P1DT2H3M4S", providerValue);

        var parsed = (IntervalDaySecond?)converter.FromProviderValue("P1DT2H3M4S", SupportedDatabase.Oracle);
        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.Value.Days);
        Assert.Equal(new TimeSpan(2, 3, 4), parsed.Value.Time);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatsIso_ForPostgres()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(months: 0, days: 1, microseconds: 0);

        var providerValue = converter.ToProviderValue(interval, SupportedDatabase.PostgreSql);
        Assert.Equal("P1D", providerValue);

        var parsed = (PostgreSqlInterval?)converter.FromProviderValue("P2D", SupportedDatabase.PostgreSql);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Value.Days);
    }

    [Fact]
    public void PostgreSqlRangeConverter_FormatsAndParses()
    {
        var converter = new PostgreSqlRangeConverter<int>();
        var range = new Range<int>(1, 10, true, false);

        var providerValue = converter.ToProviderValue(range, SupportedDatabase.PostgreSql);
        Assert.Equal("[1,10)", providerValue);

        var parsed = (Range<int>?)converter.FromProviderValue("[2,5)", SupportedDatabase.PostgreSql);
        Assert.True(parsed!.Value.HasLowerBound);
        Assert.True(parsed.Value.HasUpperBound);
        Assert.Equal(2, parsed.Value.Lower);
        Assert.Equal(5, parsed.Value.Upper);

        var fromTuple = (Range<int>?)converter.FromProviderValue(Tuple.Create<int?, int?>(3, 9), SupportedDatabase.PostgreSql);
        Assert.Equal(3, fromTuple!.Value.Lower);
        Assert.Equal(9, fromTuple.Value.Upper);
    }

    [Fact]
    public void GeometryConverter_ReadsTextAndBinary_WithSrid()
    {
        var converter = new GeometryConverter();

        var text = (Geometry?)converter.FromProviderValue("SRID=3857;POINT(1 2)", SupportedDatabase.PostgreSql);
        Assert.NotNull(text);
        Assert.Equal(3857, text!.Srid);
        Assert.Equal("POINT(1 2)", text.WellKnownText);

        var bytes = BuildEwkb(3857);
        var binary = (Geometry?)converter.FromProviderValue(bytes, SupportedDatabase.PostgreSql);
        Assert.NotNull(binary);
        Assert.Equal(3857, binary!.Srid);
    }

    [Fact]
    public void GeographyConverter_ReadsGeoJson_AndDefaultsSrid()
    {
        var converter = new GeographyConverter();
        var json = "{\"srid\":3857,\"type\":\"Point\"}";

        var fromJson = (Geography?)converter.FromProviderValue(json, SupportedDatabase.PostgreSql);
        Assert.NotNull(fromJson);
        Assert.Equal(3857, fromJson!.Srid);

        var fromText = (Geography?)converter.FromProviderValue("POINT(-74 40)", SupportedDatabase.Sqlite);
        Assert.NotNull(fromText);
        Assert.Equal(4326, fromText!.Srid);
    }

    [Fact]
    public void SpatialConverter_FormatsForMySql()
    {
        var converter = new GeometryConverter();
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);

        var providerValue = converter.ToProviderValue(geometry, SupportedDatabase.MySql);
        Assert.IsType<byte[]>(providerValue);
        Assert.Equal(Encoding.UTF8.GetBytes("POINT(1 2)"), (byte[])providerValue!);
    }

    [Fact]
    public void BlobStreamConverter_HandlesSegments_AndSeeks()
    {
        var converter = new BlobStreamConverter();
        var buffer = new byte[] { 1, 2, 3, 4, 5 };
        var segment = new ArraySegment<byte>(buffer, 1, 3);

        var stream = (Stream?)converter.FromProviderValue(segment, SupportedDatabase.PostgreSql);
        Assert.NotNull(stream);
        Assert.Equal(3, stream!.Length);

        var seekable = new MemoryStream(new byte[] { 9, 8, 7, 6 });
        seekable.Seek(2, SeekOrigin.Begin);
        var providerValue = converter.ToProviderValue(seekable, SupportedDatabase.Sqlite);
        Assert.Same(seekable, providerValue);
        Assert.Equal(0, seekable.Position);
    }

    [Fact]
    public void ClobStreamConverter_ReadsMemoryAndStreams()
    {
        var converter = new ClobStreamConverter();
        var memory = new ReadOnlyMemory<char>("hello".ToCharArray());

        var reader = (TextReader?)converter.FromProviderValue(memory, SupportedDatabase.PostgreSql);
        Assert.NotNull(reader);
        Assert.Equal("hello", reader!.ReadToEnd());

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("world"));
        var fromStream = (TextReader?)converter.FromProviderValue(stream, SupportedDatabase.PostgreSql);
        Assert.NotNull(fromStream);
        Assert.Equal("world", fromStream!.ReadToEnd());
    }

    [Fact]
    public void RowVersionConverter_RoundTripsByteArrays()
    {
        var converter = new RowVersionConverter();
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };

        var fromBytes = (RowVersion?)converter.FromProviderValue(bytes, SupportedDatabase.SqlServer);
        Assert.NotNull(fromBytes);
        Assert.Equal(bytes, fromBytes!.Value.ToArray());

        var providerValue = converter.ToProviderValue(fromBytes.Value, SupportedDatabase.SqlServer);
        Assert.Equal(bytes, providerValue);
    }

    private static byte[] BuildEwkb(int srid)
    {
        var bytes = new byte[9];
        bytes[0] = 1;
        var type = 0x20000000u;
        BitConverter.GetBytes(type).CopyTo(bytes, 1);
        BitConverter.GetBytes(srid).CopyTo(bytes, 5);
        return bytes;
    }
}
