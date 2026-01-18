using System;
using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.converters;

public static class NetworkConverterTests
{
    private static readonly InetConverter InetConverter = new();
    private static readonly CidrConverter CidrConverter = new();
    private static readonly MacAddressConverter MacConverter = new();

    [Fact]
    public static void InetConverter_UsesStringForPostgres()
    {
        var inet = new Inet(IPAddress.Parse("127.0.0.1"), 24);

        var providerValue = InetConverter.ToProviderValue(inet, SupportedDatabase.PostgreSql);

        Assert.Equal("127.0.0.1/24", providerValue);
    }

    [Fact]
    public static void InetConverter_ReadsFromString()
    {
        Assert.True(InetConverter.TryConvertFromProvider("10.0.0.1/8", SupportedDatabase.PostgreSql, out var inet));
        Assert.Equal("10.0.0.1/8", inet.ToString());
    }

    [Fact]
    public static void InetConverter_RehydratesFromNpgsqlShim()
    {
        var shim = new FakeNpgsqlInet
        {
            Address = IPAddress.Parse("192.168.0.1"),
            Netmask = 16
        };

        Assert.True(InetConverter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out var inet));
        Assert.Equal("192.168.0.1/16", inet.ToString());
    }

    [Fact]
    public static void CidrConverter_FormatsStringForPostgres()
    {
        var cidr = new Cidr(IPAddress.Parse("10.10.10.0"), 24);
        var providerValue = CidrConverter.ToProviderValue(cidr, SupportedDatabase.PostgreSql);

        Assert.Equal("10.10.10.0/24", providerValue);
    }

    [Fact]
    public static void CidrConverter_ReturnsFalseForInvalidString()
    {
        Assert.False(CidrConverter.TryConvertFromProvider("not a cidr", SupportedDatabase.PostgreSql, out _));
    }

    [Fact]
    public static void CidrConverter_ReadsFromNpgsqlShim()
    {
        var shim = new FakeNpgsqlCidr
        {
            Address = IPAddress.Parse("2001:db8::"),
            Netmask = 64
        };

        Assert.True(CidrConverter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out var cidr));
        Assert.Equal("2001:db8::/64", cidr.ToString());
    }

    [Fact]
    public static void MacAddressConverter_UsesStringForPostgres()
    {
        var mac = new MacAddress(PhysicalAddress.Parse("AA-BB-CC-DD-EE-FF"));
        var providerValue = MacConverter.ToProviderValue(mac, SupportedDatabase.PostgreSql);

        Assert.Equal("AA:BB:CC:DD:EE:FF", providerValue);
    }

    [Fact]
    public static void MacAddressConverter_CreatesFromPhysicalAddress()
    {
        var address = PhysicalAddress.Parse("00-11-22-33-44-55");
        Assert.True(MacConverter.TryConvertFromProvider(address, SupportedDatabase.SqlServer, out var mac));
        Assert.Equal("00:11:22:33:44:55", mac.ToString());
    }

    [Fact]
    public static void MacAddressConverter_FailsForUnsupportedType()
    {
        Assert.False(MacConverter.TryConvertFromProvider(123, SupportedDatabase.PostgreSql, out _));
    }

    [Fact]
    public static void AdvancedTypeConverter_ToProviderValue_ThrowsOnWrongType()
    {
        var ex = Assert.Throws<ArgumentException>(() => InetConverter.ToProviderValue(new object(), SupportedDatabase.PostgreSql));
        Assert.Contains(nameof(Inet), ex.Message);
    }

    private sealed class FakeNpgsqlInet
    {
        public IPAddress? Address { get; init; }
        public int Netmask { get; init; }
    }

    private sealed class FakeNpgsqlCidr
    {
        public IPAddress? Address { get; init; }
        public int Netmask { get; init; }
    }
}
