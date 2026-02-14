using System;
using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests edge cases for Cidr, Inet, and MacAddress value objects.
/// </summary>
public class NetworkValueObjectEdgeCaseTests
{
    // ===== Cidr =====

    [Fact]
    public void Cidr_Constructor_NullNetwork_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Cidr(null!, 24));
    }

    [Fact]
    public void Cidr_Constructor_PrefixExceedsIPv4_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Cidr(IPAddress.Parse("192.168.0.0"), 33));
        Assert.Equal("prefixLength", ex.ParamName);
    }

    [Fact]
    public void Cidr_Constructor_PrefixExceedsIPv6_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Cidr(IPAddress.IPv6Loopback, 129));
        Assert.Equal("prefixLength", ex.ParamName);
    }

    [Fact]
    public void Cidr_Constructor_IPv6Valid_Succeeds()
    {
        var cidr = new Cidr(IPAddress.Parse("2001:db8::"), 32);
        Assert.Equal(32, cidr.PrefixLength);
        Assert.NotNull(cidr.Network);
    }

    [Fact]
    public void Cidr_Equals_BothDefault_ReturnsTrue()
    {
        var a = default(Cidr);
        var b = default(Cidr);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Cidr_Equals_OneDefault_ReturnsFalse()
    {
        var a = default(Cidr);
        var b = new Cidr(IPAddress.Parse("192.168.0.0"), 24);
        Assert.False(a.Equals(b));
        Assert.False(b.Equals(a));
    }

    [Fact]
    public void Cidr_Equals_NonCidrObject_ReturnsFalse()
    {
        var cidr = new Cidr(IPAddress.Parse("192.168.0.0"), 24);
        Assert.False(cidr.Equals("not a cidr"));
        Assert.False(cidr.Equals(42));
    }

    [Fact]
    public void Cidr_Parse_EmptyString_ThrowsFormat()
    {
        Assert.Throws<FormatException>(() => Cidr.Parse(""));
        Assert.Throws<FormatException>(() => Cidr.Parse("  "));
    }

    [Fact]
    public void Cidr_Parse_NoSlash_ThrowsFormat()
    {
        Assert.Throws<FormatException>(() => Cidr.Parse("192.168.0.0"));
    }

    [Fact]
    public void Cidr_Canonicalize_PrefixZero_ZerosAllBytes()
    {
        var cidr = new Cidr(IPAddress.Parse("192.168.1.1"), 0);
        Assert.Equal(IPAddress.Parse("0.0.0.0"), cidr.Network);
    }

    [Fact]
    public void Cidr_Canonicalize_PrefixMax_PreservesAllBytes()
    {
        var cidr = new Cidr(IPAddress.Parse("192.168.1.1"), 32);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), cidr.Network);
    }

    [Fact]
    public void Cidr_Canonicalize_PartialByte_MasksCorrectly()
    {
        // /20 means 2 full bytes + 4 bits of third byte
        var cidr = new Cidr(IPAddress.Parse("192.168.255.255"), 20);
        Assert.Equal(IPAddress.Parse("192.168.240.0"), cidr.Network);
    }

    [Fact]
    public void Cidr_ToString_FormatsCorrectly()
    {
        var cidr = new Cidr(IPAddress.Parse("10.0.0.0"), 8);
        Assert.Equal("10.0.0.0/8", cidr.ToString());
    }

    [Fact]
    public void Cidr_GetHashCode_SameValues_Equal()
    {
        var a = new Cidr(IPAddress.Parse("10.0.0.0"), 8);
        var b = new Cidr(IPAddress.Parse("10.0.0.0"), 8);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Cidr_Parse_ValidString_Succeeds()
    {
        var cidr = Cidr.Parse("10.0.0.0/8");
        Assert.Equal(IPAddress.Parse("10.0.0.0"), cidr.Network);
        Assert.Equal(8, cidr.PrefixLength);
    }

    // ===== Inet =====

    [Fact]
    public void Inet_Constructor_NullAddress_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Inet(null!));
    }

    [Fact]
    public void Inet_Constructor_PrefixExceedsIPv4_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Inet(IPAddress.Parse("10.0.0.1"), 33));
        Assert.Equal("prefixLength", ex.ParamName);
    }

    [Fact]
    public void Inet_Constructor_PrefixExceedsIPv6_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Inet(IPAddress.IPv6Loopback, 129));
        Assert.Equal("prefixLength", ex.ParamName);
    }

    [Fact]
    public void Inet_Constructor_NoPrefixIPv4_Succeeds()
    {
        var inet = new Inet(IPAddress.Parse("10.0.0.1"));
        Assert.Equal(IPAddress.Parse("10.0.0.1"), inet.Address);
        Assert.Null(inet.PrefixLength);
    }

    [Fact]
    public void Inet_Constructor_WithPrefixIPv6_Succeeds()
    {
        var inet = new Inet(IPAddress.IPv6Loopback, 128);
        Assert.Equal(IPAddress.IPv6Loopback, inet.Address);
        Assert.Equal((byte)128, inet.PrefixLength);
    }

    [Fact]
    public void Inet_Equals_BothDefault_ReturnsTrue()
    {
        var a = default(Inet);
        var b = default(Inet);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Inet_Equals_OneDefault_ReturnsFalse()
    {
        var a = default(Inet);
        var b = new Inet(IPAddress.Loopback);
        Assert.False(a.Equals(b));
        Assert.False(b.Equals(a));
    }

    [Fact]
    public void Inet_Equals_NonInetObject_ReturnsFalse()
    {
        var inet = new Inet(IPAddress.Loopback);
        Assert.False(inet.Equals("not inet"));
        Assert.False(inet.Equals(42));
    }

    [Fact]
    public void Inet_Parse_EmptyString_ThrowsFormat()
    {
        Assert.Throws<FormatException>(() => Inet.Parse(""));
        Assert.Throws<FormatException>(() => Inet.Parse("  "));
    }

    [Fact]
    public void Inet_Parse_IPv6WithPrefix_Succeeds()
    {
        var inet = Inet.Parse("::1/128");
        Assert.Equal(IPAddress.IPv6Loopback, inet.Address);
        Assert.Equal((byte)128, inet.PrefixLength);
    }

    [Fact]
    public void Inet_Parse_WithoutPrefix_Succeeds()
    {
        var inet = Inet.Parse("10.0.0.1");
        Assert.Equal(IPAddress.Parse("10.0.0.1"), inet.Address);
        Assert.Null(inet.PrefixLength);
    }

    [Fact]
    public void Inet_ToString_WithPrefix()
    {
        var inet = new Inet(IPAddress.Parse("10.0.0.1"), 24);
        Assert.Equal("10.0.0.1/24", inet.ToString());
    }

    [Fact]
    public void Inet_ToString_WithoutPrefix()
    {
        var inet = new Inet(IPAddress.Parse("10.0.0.1"));
        Assert.Equal("10.0.0.1", inet.ToString());
    }

    [Fact]
    public void Inet_GetHashCode_SameValues_Equal()
    {
        var a = new Inet(IPAddress.Loopback, 24);
        var b = new Inet(IPAddress.Loopback, 24);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ===== MacAddress =====

    [Fact]
    public void MacAddress_Constructor_NullAddress_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MacAddress(null!));
    }

    [Fact]
    public void MacAddress_ToString_EmptyBytes_ReturnsEmpty()
    {
        var mac = new MacAddress(new PhysicalAddress(Array.Empty<byte>()));
        Assert.Equal(string.Empty, mac.ToString());
    }

    [Fact]
    public void MacAddress_Equals_BothDefault_ReturnsTrue()
    {
        var a = default(MacAddress);
        var b = default(MacAddress);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void MacAddress_Equals_OneDefault_ReturnsFalse()
    {
        var a = default(MacAddress);
        var b = new MacAddress(new PhysicalAddress(new byte[] { 0x08, 0x00, 0x2B, 0x01, 0x02, 0x03 }));
        Assert.False(a.Equals(b));
        Assert.False(b.Equals(a));
    }

    [Fact]
    public void MacAddress_Equals_NonMacAddressObject_ReturnsFalse()
    {
        var mac = new MacAddress(new PhysicalAddress(new byte[] { 0x08, 0x00, 0x2B, 0x01, 0x02, 0x03 }));
        Assert.False(mac.Equals("not a mac"));
        Assert.False(mac.Equals(42));
    }

    [Fact]
    public void MacAddress_Parse_EmptyString_ThrowsArgument()
    {
        Assert.Throws<ArgumentException>(() => MacAddress.Parse(""));
        Assert.Throws<ArgumentException>(() => MacAddress.Parse("  "));
    }

    [Fact]
    public void MacAddress_Parse_InvalidHex_ThrowsFormat()
    {
        Assert.Throws<FormatException>(() => MacAddress.Parse("ZZ:ZZ:ZZ:ZZ:ZZ:ZZ"));
    }

    [Fact]
    public void MacAddress_Parse_OddLength_ThrowsFormat()
    {
        // After normalization (removing separators), odd number of hex chars throws
        Assert.Throws<FormatException>(() => MacAddress.Parse("08002"));
    }

    [Fact]
    public void MacAddress_Parse_RawHex_Succeeds()
    {
        var mac = MacAddress.Parse("08002B010203");
        Assert.Equal("08:00:2B:01:02:03", mac.ToString());
    }

    [Fact]
    public void MacAddress_Parse_HyphenSeparated_Succeeds()
    {
        var mac = MacAddress.Parse("08-00-2B-01-02-03");
        Assert.Equal("08:00:2B:01:02:03", mac.ToString());
    }

    [Fact]
    public void MacAddress_Parse_ColonSeparated_Succeeds()
    {
        var mac = MacAddress.Parse("08:00:2b:01:02:03");
        Assert.Equal("08:00:2B:01:02:03", mac.ToString());
    }

    [Fact]
    public void MacAddress_Parse_DotSeparated_Succeeds()
    {
        var mac = MacAddress.Parse("0800.2B01.0203");
        Assert.Equal("08:00:2B:01:02:03", mac.ToString());
    }

    [Fact]
    public void MacAddress_GetHashCode_SameValues_Equal()
    {
        var a = MacAddress.Parse("08:00:2B:01:02:03");
        var b = MacAddress.Parse("08:00:2B:01:02:03");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MacAddress_Equals_SameValues_ReturnsTrue()
    {
        var a = MacAddress.Parse("08:00:2B:01:02:03");
        var b = MacAddress.Parse("08:00:2B:01:02:03");
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
    }

    [Fact]
    public void MacAddress_8ByteAddress_Succeeds()
    {
        var mac = new MacAddress(new PhysicalAddress(new byte[] { 0x08, 0x00, 0x2B, 0x01, 0x02, 0x03, 0x04, 0x05 }));
        Assert.Equal("08:00:2B:01:02:03:04:05", mac.ToString());
    }
}
