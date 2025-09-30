using System;
using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class ValueObjectTests
{
    #region Network Value Objects

    [Fact]
    public void Inet_ShouldCreateFromIPAddress()
    {
        var address = IPAddress.Parse("192.168.1.1");
        var inet = new Inet(address);

        Assert.Equal(address, inet.Address);
        Assert.Null(inet.PrefixLength);
    }

    [Fact]
    public void Inet_ShouldCreateWithPrefix()
    {
        var address = IPAddress.Parse("192.168.1.1");
        var inet = new Inet(address, 24);

        Assert.Equal(address, inet.Address);
        Assert.Equal((byte?)24, inet.PrefixLength);
    }

    [Fact]
    public void Inet_ToString_ShouldFormatCorrectly()
    {
        var inet1 = new Inet(IPAddress.Parse("10.0.0.1"));
        var inet2 = new Inet(IPAddress.Parse("192.168.1.0"), 24);

        Assert.Equal("10.0.0.1", inet1.ToString());
        Assert.Equal("192.168.1.0/24", inet2.ToString());
    }

    [Fact]
    public void Inet_ShouldCreateWithoutPrefix()
    {
        var address = IPAddress.Parse("10.0.0.1");
        var inet = new Inet(address);

        Assert.Equal(address, inet.Address);
        Assert.Null(inet.PrefixLength);
    }

    [Fact]
    public void Inet_ShouldCreateWithPrefixLength()
    {
        var address = IPAddress.Parse("192.168.1.0");
        var inet = new Inet(address, 24);

        Assert.Equal(address, inet.Address);
        Assert.Equal((byte?)24, inet.PrefixLength);
    }

    [Fact]
    public void Cidr_ShouldRequirePrefix()
    {
        var address = IPAddress.Parse("192.168.0.0");
        var cidr = new Cidr(address, 16);

        Assert.Equal(address, cidr.Network);
        Assert.Equal((byte)16, cidr.PrefixLength);
    }

    [Fact]
    public void Cidr_ToString_ShouldIncludePrefix()
    {
        var cidr = new Cidr(IPAddress.Parse("10.0.0.0"), 8);
        Assert.Equal("10.0.0.0/8", cidr.ToString());
    }

    [Fact]
    public void MacAddress_ShouldCreateFromPhysicalAddress()
    {
        var physical = new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 });
        var mac = new MacAddress(physical);

        Assert.Equal(physical, mac.Address);
    }

    [Fact]
    public void MacAddress_Parse_ShouldWorkWithColonFormat()
    {
        var mac = MacAddress.Parse("00:11:22:33:44:55");

        Assert.NotNull(mac.Address);
        var bytes = mac.Address.GetAddressBytes();
        Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }, bytes);
    }

    [Fact]
    public void MacAddress_Parse_ShouldWorkWithDashFormat()
    {
        var mac = MacAddress.Parse("00-11-22-33-44-55");

        Assert.NotNull(mac.Address);
        var bytes = mac.Address.GetAddressBytes();
        Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }, bytes);
    }

    #endregion

    #region Range Value Objects

    [Fact]
    public void Range_ShouldCreateInclusiveRange()
    {
        var range = new Range<int>(1, 10, true, true);

        Assert.Equal(1, range.Lower);
        Assert.Equal(10, range.Upper);
        Assert.True(range.IsLowerInclusive);
        Assert.True(range.IsUpperInclusive);
        Assert.True(range.HasLowerBound);
        Assert.True(range.HasUpperBound);
    }

    [Fact]
    public void Range_ShouldCreateExclusiveRange()
    {
        var range = new Range<int>(1, 10, false, false);

        Assert.Equal(1, range.Lower);
        Assert.Equal(10, range.Upper);
        Assert.False(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_ShouldCreateUnboundedRange()
    {
        var range = new Range<int?>(null, null);

        Assert.False(range.HasLowerBound);
        Assert.False(range.HasUpperBound);
        Assert.True(range.IsEmpty); // The property is IsEmpty, not IsUnbounded
    }

    [Fact]
    public void Range_Empty_ShouldReturnEmptyRange()
    {
        var empty = Range<int>.Empty;

        Assert.True(empty.IsEmpty);
        Assert.False(empty.HasLowerBound);
        Assert.False(empty.HasUpperBound);
    }

    [Fact]
    public void Range_ShouldHandleBounds()
    {
        var range = new Range<int>(1, 10, true, false); // [1, 10)

        Assert.True(range.HasLowerBound);
        Assert.True(range.HasUpperBound);
        Assert.Equal(1, range.Lower);
        Assert.Equal(10, range.Upper);
        Assert.True(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_ToString_ShouldFormatCorrectly()
    {
        var range1 = new Range<int>(1, 10, true, false);
        var range2 = new Range<int>(1, 10, false, true);

        Assert.Contains("1", range1.ToString());
        Assert.Contains("10", range1.ToString());
        Assert.Contains("1", range2.ToString());
        Assert.Contains("10", range2.ToString());
    }

    #endregion

    #region Interval Value Objects

    [Fact]
    public void IntervalYearMonth_ShouldCreateCorrectly()
    {
        var interval = new IntervalYearMonth(2, 6);

        Assert.Equal(2, interval.Years);
        Assert.Equal(6, interval.Months);
        Assert.Equal(30, interval.TotalMonths); // 2*12 + 6
    }

    [Fact]
    public void IntervalYearMonth_ToString_ShouldFormatCorrectly()
    {
        var interval = new IntervalYearMonth(1, 3);
        var result = interval.ToString();

        Assert.Contains("1", result); // Should contain years
        Assert.Contains("3", result); // Should contain months
    }

    [Fact]
    public void IntervalDaySecond_ShouldCreateFromComponents()
    {
        var time = TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(30));
        var interval = new IntervalDaySecond(5, time);

        Assert.Equal(5, interval.Days);
        Assert.Equal(time, interval.Time);
    }

    [Fact]
    public void IntervalDaySecond_FromTimeSpan_ShouldWorkCorrectly()
    {
        var timeSpan = TimeSpan.FromDays(3).Add(TimeSpan.FromHours(6));
        var interval = IntervalDaySecond.FromTimeSpan(timeSpan);

        Assert.Equal(3, interval.Days);
        Assert.Equal(TimeSpan.FromHours(6), interval.Time);
    }

    [Fact]
    public void PostgreSqlInterval_ShouldCreateFromComponents()
    {
        var interval = new PostgreSqlInterval(12, 30, 3600000000); // 12 months, 30 days, 1 hour in microseconds

        Assert.Equal(12, interval.Months);
        Assert.Equal(30, interval.Days);
        Assert.Equal(3600000000, interval.Microseconds);
    }

    [Fact]
    public void PostgreSqlInterval_FromTimeSpan_ShouldConvertCorrectly()
    {
        var timeSpan = TimeSpan.FromHours(2.5);
        var interval = PostgreSqlInterval.FromTimeSpan(timeSpan);

        Assert.Equal(0, interval.Months);
        Assert.Equal(0, interval.Days);
        Assert.Equal((long)(timeSpan.Ticks / 10), interval.Microseconds); // Convert ticks to microseconds
    }

    #endregion

    #region Concurrency Token Value Objects

    [Fact]
    public void RowVersion_ShouldCreateFromBytes()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        var rowVersion = RowVersion.FromBytes(bytes);

        Assert.Equal(bytes, rowVersion.ToArray());
    }

    [Fact]
    public void RowVersion_ShouldThrowOnInvalidLength()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 }; // Wrong length

        Assert.Throws<ArgumentException>(() => RowVersion.FromBytes(bytes));
    }

    [Fact]
    public void RowVersion_ShouldCompareCorrectly()
    {
        var bytes1 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        var bytes2 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 };

        var rv1 = RowVersion.FromBytes(bytes1);
        var rv2 = RowVersion.FromBytes(bytes2);
        var rv3 = RowVersion.FromBytes(bytes1); // Same as rv1

        Assert.True(rv1.Equals(rv3));
        Assert.False(rv1.Equals(rv2));
        Assert.Equal(rv1.GetHashCode(), rv3.GetHashCode());
    }

    [Fact]
    public void RowVersion_ToString_ShouldFormatAsHex()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF };
        var rowVersion = RowVersion.FromBytes(bytes);

        var result = rowVersion.ToString();
        Assert.Contains("FF", result.ToUpper());
    }

    #endregion

    #region Spatial Value Objects

    [Fact]
    public void Geometry_FromWellKnownText_ShouldCreateCorrectly()
    {
        var wkt = "POINT(1 2)";
        var geometry = Geometry.FromWellKnownText(wkt, 0);

        Assert.Equal(wkt, geometry.WellKnownText);
        Assert.Equal(0, geometry.Srid);
        Assert.Null(geometry.ProviderValue);
    }

    [Fact]
    public void Geometry_FromWellKnownText_WithSRID_ShouldSetSRID()
    {
        var wkt = "POINT(1 2)";
        var srid = 4326;
        var geometry = Geometry.FromWellKnownText(wkt, srid);

        Assert.Equal(wkt, geometry.WellKnownText);
        Assert.Equal(srid, geometry.Srid);
    }

    [Fact]
    public void Geometry_FromGeoJson_ShouldCreateCorrectly()
    {
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[1,2]}";
        var geometry = Geometry.FromGeoJson(geoJson, 0);

        Assert.Equal(geoJson, geometry.GeoJson);
        Assert.Equal(0, geometry.Srid);
    }

    [Fact]
    public void Geography_ShouldUseProvidedSRID()
    {
        var wkt = "POINT(1 2)";
        var geography = Geography.FromWellKnownText(wkt, 4326);

        Assert.Equal(wkt, geography.WellKnownText);
        Assert.Equal(4326, geography.Srid);
    }

    [Fact]
    public void SpatialValue_WithProviderValue_ShouldSetProviderValue()
    {
        var geometry = Geometry.FromWellKnownText("POINT(1 2)", 0);
        var providerValue = new object();

        var withProvider = geometry.WithProviderValue(providerValue);

        Assert.Same(providerValue, withProvider.ProviderValue);
        Assert.Equal(geometry.WellKnownText, withProvider.WellKnownText);
        Assert.Equal(geometry.Srid, withProvider.Srid);
    }

    #endregion
}