using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using Moq;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedCoercionsTests
{
    [Fact]
    public void PostgreSqlIntervalCoercion_ReadsTimeSpan()
    {
        var coercion = new ConverterRead<PostgreSqlInterval>();
        var span = TimeSpan.FromHours(3);

        Assert.True(coercion.TryRead(new DbValue(span), out var interval));
        Assert.Equal(span, interval.ToTimeSpan());

    }

    [Fact]
    public void IntervalYearMonthCoercion_ReadsString()
    {
        var coercion = new ConverterRead<IntervalYearMonth>();

        Assert.True(coercion.TryRead(new DbValue("P2Y3M", typeof(string)), out var interval));
        Assert.Equal(2, interval.Years);
        Assert.Equal(3, interval.Months);

    }

    [Fact]
    public void IntervalDaySecondCoercion_ReadsTimeSpanAndString()
    {
        var coercion = new ConverterRead<IntervalDaySecond>();
        var span = TimeSpan.FromDays(1) + TimeSpan.FromMinutes(5);

        Assert.True(coercion.TryRead(new DbValue(span), out var fromSpan));
        Assert.Equal(span, fromSpan.TotalTime);

        Assert.True(coercion.TryRead(new DbValue("P2DT3H4M5S", typeof(string)), out var parsed));
        Assert.Equal(2, parsed.Days);
        Assert.Equal(new TimeSpan(3, 4, 5), parsed.Time);

    }

    [Fact]
    public void InetCoercion_ReadsMultipleInputs()
    {
        var coercion = new ConverterRead<Inet>();
        var ip = IPAddress.Parse("10.0.0.1");

        Assert.True(coercion.TryRead(new DbValue("10.0.0.1/24", typeof(string)), out var fromString));
        Assert.Equal("10.0.0.1/24", fromString.ToString());

        Assert.True(coercion.TryRead(new DbValue(ip), out var fromIp));
        Assert.Equal(ip, fromIp.Address);

    }

    [Fact]
    public void CidrCoercion_ReadsString()
    {
        var coercion = new ConverterRead<Cidr>();

        Assert.True(coercion.TryRead(new DbValue("192.168.0.0/16", typeof(string)), out var cidr));
        Assert.Equal("192.168.0.0/16", cidr.ToString());

    }

    [Fact]
    public void MacAddressCoercion_ReadsMultipleInputs()
    {
        var coercion = new ConverterRead<MacAddress>();
        var physical = new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 });

        Assert.True(coercion.TryRead(new DbValue("00:11:22:33:44:55", typeof(string)), out var fromString));
        Assert.True(coercion.TryRead(new DbValue(physical), out var fromPhysical));
        Assert.Equal(physical, fromPhysical.Address);

    }

    [Fact]
    public void GeometryCoercion_ReadsBinaryAndText()
    {
        var coercion = new ConverterRead<Geometry>();
        var bytes = new byte[] { 1, 2, 3 };

        Assert.True(coercion.TryRead(new DbValue(bytes), out var fromBytes));
        Assert.Equal(bytes, fromBytes.WellKnownBinary.ToArray());

        Assert.True(coercion.TryRead(new DbValue("POINT(1 2)", typeof(string)), out var fromText));
        Assert.Equal("POINT(1 2)", fromText.WellKnownText);

    }

    [Fact]
    public void GeographyCoercion_ReadsBinaryAndGeoJson()
    {
        var coercion = new ConverterRead<Geography>();
        var bytes = new byte[] { 4, 5, 6 };
        var json = "{\"type\":\"Point\"}";

        Assert.True(coercion.TryRead(new DbValue(bytes), out var fromBytes));
        Assert.Equal(bytes, fromBytes.WellKnownBinary.ToArray());

        Assert.True(coercion.TryRead(new DbValue(json, typeof(string)), out var fromJson));
        Assert.Equal(json, fromJson.GeoJson);

    }

    [Fact]
    public void PostgreSqlRangeCoercions_ReadStringValues()
    {
        var intCoercion = new ConverterRead<Range<int>>();
        var dateCoercion = new ConverterRead<Range<DateTime>>();
        var longCoercion = new ConverterRead<Range<long>>();

        Assert.True(intCoercion.TryRead(new DbValue("[1,10)", typeof(string)), out var intRange));
        Assert.Equal(1, intRange.Lower);
        Assert.Equal(10, intRange.Upper);

        Assert.True(dateCoercion.TryRead(new DbValue("[2023-01-01,2023-02-01)", typeof(string)), out var dateRange));
        Assert.Equal(new DateTime(2023, 1, 1), dateRange.Lower);
        Assert.Equal(new DateTime(2023, 2, 1), dateRange.Upper);

        Assert.True(longCoercion.TryRead(new DbValue("[100,200)", typeof(string)), out var longRange));
        Assert.Equal(100L, longRange.Lower);
        Assert.Equal(200L, longRange.Upper);
    }

    [Fact]
    public void RowVersionValueCoercion_ReadsBytesAndUlong()
    {
        var coercion = new ConverterRead<RowVersion>();
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };

        Assert.True(coercion.TryRead(new DbValue(bytes), out var fromBytes));
        Assert.Equal(bytes, fromBytes.ToArray());

        const ulong value = 1;
        var expected = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(expected);
        }

        Assert.True(coercion.TryRead(new DbValue(value), out var fromUlong));
        Assert.Equal(expected, fromUlong.ToArray());

    }

    [Fact]
    public void BlobStreamCoercion_ReadsStreamAndBytes()
    {
        var coercion = new ConverterRead<System.IO.Stream>();
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        stream.Seek(2, SeekOrigin.Begin);

        Assert.True(coercion.TryRead(new DbValue(stream), out var fromStream));
        Assert.Same(stream, fromStream);
        Assert.Equal(0, stream.Position);

        Assert.True(coercion.TryRead(new DbValue(new byte[] { 9, 8, 7 }), out var fromBytes));
        Assert.Equal(3, fromBytes.Length);

    }

    [Fact]
    public void ClobStreamCoercion_ReadsStringAndStream()
    {
        var coercion = new ConverterRead<System.IO.TextReader>();

        Assert.True(coercion.TryRead(new DbValue("hello", typeof(string)), out var fromString));
        Assert.Equal("hello", fromString.ReadToEnd());

        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("stream"));
        Assert.True(coercion.TryRead(new DbValue(stream), out var fromStream));
        Assert.Equal("stream", fromStream.ReadToEnd());

    }

}