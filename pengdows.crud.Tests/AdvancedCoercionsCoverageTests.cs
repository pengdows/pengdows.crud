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

public class AdvancedCoercionsCoverageTests
{
    [Fact]
    public void PostgreSqlIntervalCoercion_ReadsTimeSpan_AndWrites()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var span = TimeSpan.FromHours(3);

        Assert.True(coercion.TryRead(new DbValue(span), out var interval));
        Assert.Equal(span, interval.ToTimeSpan());

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(interval, param.Object));
        param.VerifySet(p => p.Value = interval.ToTimeSpan(), Times.Once);
        param.VerifySet(p => p.DbType = DbType.Object, Times.Once);
    }

    [Fact]
    public void IntervalYearMonthCoercion_ReadsString_AndWrites()
    {
        var coercion = new IntervalYearMonthCoercion();

        Assert.True(coercion.TryRead(new DbValue("P2Y3M", typeof(string)), out var interval));
        Assert.Equal(2, interval.Years);
        Assert.Equal(3, interval.Months);

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(interval, param.Object));
        param.VerifySet(p => p.Value = "P2Y3M", Times.Once);
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void IntervalDaySecondCoercion_ReadsTimeSpanAndString_AndWrites()
    {
        var coercion = new IntervalDaySecondCoercion();
        var span = TimeSpan.FromDays(1) + TimeSpan.FromMinutes(5);

        Assert.True(coercion.TryRead(new DbValue(span), out var fromSpan));
        Assert.Equal(span, fromSpan.TotalTime);

        Assert.True(coercion.TryRead(new DbValue("P2DT3H4M5S", typeof(string)), out var parsed));
        Assert.Equal(2, parsed.Days);
        Assert.Equal(new TimeSpan(3, 4, 5), parsed.Time);

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(parsed, param.Object));
        param.VerifySet(p => p.Value = parsed.TotalTime, Times.Once);
        param.VerifySet(p => p.DbType = DbType.Object, Times.Once);
    }

    [Fact]
    public void InetCoercion_ReadsMultipleInputs_AndWrites()
    {
        var coercion = new InetCoercion();
        var ip = IPAddress.Parse("10.0.0.1");

        Assert.True(coercion.TryRead(new DbValue("10.0.0.1/24", typeof(string)), out var fromString));
        Assert.Equal("10.0.0.1/24", fromString.ToString());

        Assert.True(coercion.TryRead(new DbValue(ip), out var fromIp));
        Assert.Equal(ip, fromIp.Address);

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(fromString, param.Object));
        param.VerifySet(p => p.Value = "10.0.0.1/24", Times.Once);
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void CidrCoercion_ReadsString_AndWrites()
    {
        var coercion = new CidrCoercion();

        Assert.True(coercion.TryRead(new DbValue("192.168.0.0/16", typeof(string)), out var cidr));
        Assert.Equal("192.168.0.0/16", cidr.ToString());

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(cidr, param.Object));
        param.VerifySet(p => p.Value = "192.168.0.0/16", Times.Once);
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void MacAddressCoercion_ReadsMultipleInputs_AndWrites()
    {
        var coercion = new MacAddressCoercion();
        var physical = new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 });

        Assert.True(coercion.TryRead(new DbValue("00:11:22:33:44:55", typeof(string)), out var fromString));
        Assert.True(coercion.TryRead(new DbValue(physical), out var fromPhysical));
        Assert.Equal(physical, fromPhysical.Address);

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(fromString, param.Object));
        param.VerifySet(p => p.Value = "00:11:22:33:44:55", Times.Once);
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void GeometryCoercion_ReadsBinaryAndText_AndWrites()
    {
        var coercion = new GeometryCoercion();
        var bytes = new byte[] { 1, 2, 3 };

        Assert.True(coercion.TryRead(new DbValue(bytes), out var fromBytes));
        Assert.Equal(bytes, fromBytes.WellKnownBinary.ToArray());

        Assert.True(coercion.TryRead(new DbValue("POINT(1 2)", typeof(string)), out var fromText));
        Assert.Equal("POINT(1 2)", fromText.WellKnownText);

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(Geometry.FromWellKnownBinary(bytes, 0), param.Object));
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);

        var textParam = new Mock<DbParameter>();
        textParam.SetupAllProperties();

        Assert.True(coercion.TryWrite(Geometry.FromWellKnownText("POINT(1 2)", 0), textParam.Object));
        textParam.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void GeographyCoercion_ReadsBinaryAndGeoJson_AndWrites()
    {
        var coercion = new GeographyCoercion();
        var bytes = new byte[] { 4, 5, 6 };
        var json = "{\"type\":\"Point\"}";

        Assert.True(coercion.TryRead(new DbValue(bytes), out var fromBytes));
        Assert.Equal(bytes, fromBytes.WellKnownBinary.ToArray());

        Assert.True(coercion.TryRead(new DbValue(json, typeof(string)), out var fromJson));
        Assert.Equal(json, fromJson.GeoJson);

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(Geography.FromWellKnownBinary(bytes, 4326), param.Object));
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);

        var textParam = new Mock<DbParameter>();
        textParam.SetupAllProperties();

        Assert.True(coercion.TryWrite(Geography.FromWellKnownText("POINT(1 2)", 4326), textParam.Object));
        textParam.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }

    [Fact]
    public void PostgreSqlRangeCoercions_ReadStringValues()
    {
        var intCoercion = new PostgreSqlRangeIntCoercion();
        var dateCoercion = new PostgreSqlRangeDateTimeCoercion();
        var longCoercion = new PostgreSqlRangeLongCoercion();

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
    public void RowVersionValueCoercion_ReadsBytesAndUlong_AndWrites()
    {
        var coercion = new RowVersionValueCoercion();
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

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(fromBytes, param.Object));
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);
        param.VerifySet(p => p.Size = 8, Times.Once);
    }

    [Fact]
    public void BlobStreamCoercion_ReadsStreamAndBytes_AndWrites()
    {
        var coercion = new BlobStreamCoercion();
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        stream.Seek(2, SeekOrigin.Begin);

        Assert.True(coercion.TryRead(new DbValue(stream), out var fromStream));
        Assert.Same(stream, fromStream);
        Assert.Equal(0, stream.Position);

        Assert.True(coercion.TryRead(new DbValue(new byte[] { 9, 8, 7 }), out var fromBytes));
        Assert.Equal(3, fromBytes.Length);

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(stream, param.Object));
        param.VerifySet(p => p.DbType = DbType.Binary, Times.Once);
    }

    [Fact]
    public void ClobStreamCoercion_ReadsStringAndStream_AndWrites()
    {
        var coercion = new ClobStreamCoercion();

        Assert.True(coercion.TryRead(new DbValue("hello", typeof(string)), out var fromString));
        Assert.Equal("hello", fromString.ReadToEnd());

        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("stream"));
        Assert.True(coercion.TryRead(new DbValue(stream), out var fromStream));
        Assert.Equal("stream", fromStream.ReadToEnd());

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();

        Assert.True(coercion.TryWrite(new StringReader("value"), param.Object));
        param.VerifySet(p => p.DbType = DbType.String, Times.Once);
    }
}
