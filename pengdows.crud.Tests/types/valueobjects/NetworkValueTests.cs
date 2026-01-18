using System;
using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.valueobjects;

public static class NetworkValueTests
{
    [Fact]
    public static void Inet_ToString_IncludesPrefixWhenSupplied()
    {
        var inet = new Inet(IPAddress.Parse("192.168.1.10"), 24);
        Assert.Equal("192.168.1.10/24", inet.ToString());
    }

    [Fact]
    public static void Inet_ThrowsWhenPrefixExceedsFamily()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inet(IPAddress.Parse("10.0.0.1"), 33));
    }

    [Fact]
    public static void Cidr_CanonicalizesHostBits()
    {
        var cidr = new Cidr(IPAddress.Parse("10.0.0.7"), 24);

        Assert.Equal("10.0.0.0/24", cidr.ToString());
    }

    [Fact]
    public static void Cidr_ThrowsWhenNetworkIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Cidr(null!, 16));
    }

    [Fact]
    public static void MacAddress_Parse_NormalizesSeparatorsAndCasing()
    {
        var mac = MacAddress.Parse("aa-bb-cc-dd-ee-ff");
        Assert.Equal("AA:BB:CC:DD:EE:FF", mac.ToString());
    }

    [Fact]
    public static void MacAddress_Parse_ThrowsOnOddLength()
    {
        Assert.Throws<FormatException>(() => MacAddress.Parse("AA:BB:CC:D"));
    }

    [Fact]
    public static void MacAddress_Equals_ComparesUnderlyingAddress()
    {
        var first = new MacAddress(PhysicalAddress.Parse("00-11-22-33-44-55"));
        var second = new MacAddress(PhysicalAddress.Parse("00-11-22-33-44-55"));
        var different = new MacAddress(PhysicalAddress.Parse("00-11-22-33-44-00"));

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
    }
}
