using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedCoercionsBranchTests
{
    private sealed class ProviderInet
    {
        public IPAddress Address { get; init; } = IPAddress.Loopback;
        public byte Netmask { get; init; }
    }

    private sealed class ProviderInetNoNetmask
    {
        public IPAddress Address { get; init; } = IPAddress.Loopback;
    }

    private sealed class ProviderCidr
    {
        public IPAddress Address { get; init; } = IPAddress.Loopback;
        public byte Netmask { get; init; }
    }

    private sealed class ProviderCidrInvalid
    {
        public IPAddress Address { get; init; } = IPAddress.Loopback;
        public string Netmask { get; init; } = "invalid";
    }

    private sealed class ProviderMacAddress
    {
        public PhysicalAddress Address { get; init; } = PhysicalAddress.None;
    }

    private sealed class ProviderMacAddressInvalid
    {
        public string Address { get; init; } = "invalid";
    }

    [Fact]
    public void IntervalYearMonthCoercion_HandlesNullAndInvalid()
    {
        var coercion = new IntervalYearMonthCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));
        Assert.False(coercion.TryRead(new DbValue("P9999999999999999999999Y", typeof(string)), out _));

        var interval = new IntervalYearMonth(1, 2);
        Assert.True(coercion.TryRead(new DbValue(interval), out var fromInterval));
        Assert.Equal(interval, fromInterval);
    }

    [Fact]
    public void IntervalDaySecondCoercion_HandlesNullAndInvalid()
    {
        var coercion = new IntervalDaySecondCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));
        Assert.False(coercion.TryRead(new DbValue("P9999999999999999999999D", typeof(string)), out _));

        var interval = new IntervalDaySecond(1, TimeSpan.FromHours(2));
        Assert.True(coercion.TryRead(new DbValue(interval), out var fromInterval));
        Assert.Equal(interval, fromInterval);
    }

    [Fact]
    public void InetCoercion_HandlesNullAndProviderSpecific()
    {
        var coercion = new InetCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));

        var inetValue = new Inet(IPAddress.Parse("10.0.0.1"), 24);
        Assert.True(coercion.TryRead(new DbValue(inetValue), out var fromInet));
        Assert.Equal(inetValue, fromInet);

        var provider = new ProviderInet
        {
            Address = IPAddress.Parse("10.0.0.1"),
            Netmask = 24
        };

        Assert.True(coercion.TryRead(new DbValue(provider), out var inet));
        Assert.Equal(IPAddress.Parse("10.0.0.1"), inet.Address);
        Assert.Equal((byte)24, inet.PrefixLength);

        var providerNoNetmask = new ProviderInetNoNetmask
        {
            Address = IPAddress.Parse("10.0.0.2")
        };
        Assert.True(coercion.TryRead(new DbValue(providerNoNetmask), out var inetNoMask));
        Assert.Null(inetNoMask.PrefixLength);
    }

    [Fact]
    public void CidrCoercion_HandlesNullAndProviderSpecific()
    {
        var coercion = new CidrCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));

        var cidrValue = Cidr.Parse("192.168.0.0/16");
        Assert.True(coercion.TryRead(new DbValue(cidrValue), out var fromCidr));
        Assert.Equal(cidrValue, fromCidr);

        var provider = new ProviderCidr
        {
            Address = IPAddress.Parse("192.168.0.0"),
            Netmask = 16
        };

        Assert.True(coercion.TryRead(new DbValue(provider), out var cidr));
        Assert.Equal("192.168.0.0/16", cidr.ToString());

        var invalidProvider = new ProviderCidrInvalid
        {
            Address = IPAddress.Parse("10.0.0.0")
        };
        Assert.False(coercion.TryRead(new DbValue(invalidProvider), out _));
    }

    [Fact]
    public void MacAddressCoercion_HandlesNullAndProviderSpecific()
    {
        var coercion = new MacAddressCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));

        var macValue = MacAddress.Parse("00:11:22:33:44:55");
        Assert.True(coercion.TryRead(new DbValue(macValue), out var fromMac));
        Assert.Equal(macValue, fromMac);

        var provider = new ProviderMacAddress
        {
            Address = new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 })
        };

        Assert.True(coercion.TryRead(new DbValue(provider), out var mac));
        Assert.Equal(provider.Address, mac.Address);

        var invalidProvider = new ProviderMacAddressInvalid();
        Assert.False(coercion.TryRead(new DbValue(invalidProvider), out _));
    }

    [Fact]
    public void GeometryCoercion_HandlesNullAndGeoJson()
    {
        var coercion = new GeometryCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));

        var geomValue = Geometry.FromWellKnownText("POINT(1 2)", 0);
        Assert.True(coercion.TryRead(new DbValue(geomValue), out var fromGeom));
        Assert.Equal(geomValue, fromGeom);

        Assert.True(coercion.TryRead(new DbValue("{\"type\":\"Point\"}", typeof(string)), out var geom));
        Assert.Equal(SpatialFormat.GeoJson, geom.Format);
    }

    [Fact]
    public void GeographyCoercion_HandlesNullAndGeoJson()
    {
        var coercion = new GeographyCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));

        Assert.True(coercion.TryRead(new DbValue("POINT(1 2)", typeof(string)), out var fromWkt));
        Assert.Equal(SpatialFormat.WellKnownText, fromWkt.Format);

        Assert.True(coercion.TryRead(new DbValue("{\"type\":\"Point\"}", typeof(string)), out var geog));
        Assert.Equal(SpatialFormat.GeoJson, geog.Format);
    }

    [Fact]
    public void RangeCoercions_HandleNullAndInvalid()
    {
        var intCoercion = new PostgreSqlRangeIntCoercion();
        var dateCoercion = new PostgreSqlRangeDateTimeCoercion();
        var longCoercion = new PostgreSqlRangeLongCoercion();

        Assert.False(intCoercion.TryRead(new DbValue(null), out _));
        Assert.False(dateCoercion.TryRead(new DbValue(null), out _));
        Assert.False(longCoercion.TryRead(new DbValue(null), out _));

        Assert.False(intCoercion.TryRead(new DbValue("bad", typeof(string)), out _));
        Assert.False(dateCoercion.TryRead(new DbValue("bad", typeof(string)), out _));
        Assert.False(longCoercion.TryRead(new DbValue("bad", typeof(string)), out _));

        var intRange = new Range<int>(1, 2);
        var dateRange = new Range<DateTime>(new DateTime(2024, 1, 1), new DateTime(2024, 2, 1));
        var longRange = new Range<long>(1, 2);

        Assert.True(intCoercion.TryRead(new DbValue(intRange), out var fromInt));
        Assert.Equal(intRange, fromInt);
        Assert.True(dateCoercion.TryRead(new DbValue(dateRange), out var fromDate));
        Assert.Equal(dateRange, fromDate);
        Assert.True(longCoercion.TryRead(new DbValue(longRange), out var fromLong));
        Assert.Equal(longRange, fromLong);
    }

    [Fact]
    public void BlobStreamCoercion_HandlesNullAndMemory()
    {
        var coercion = new BlobStreamCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));

        var memory = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });
        Assert.True(coercion.TryRead(new DbValue(memory), out var stream));
        Assert.Equal(3, stream.Length);

        Assert.False(coercion.TryRead(new DbValue(123), out _));
    }

    [Fact]
    public void ClobStreamCoercion_HandlesNullAndDefault()
    {
        var coercion = new ClobStreamCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));
        Assert.False(coercion.TryRead(new DbValue(123), out _));

        var reader = new StringReader("value");
        Assert.True(coercion.TryRead(new DbValue(reader), out var fromReader));
        Assert.Equal("value", fromReader.ReadToEnd());
    }
}
