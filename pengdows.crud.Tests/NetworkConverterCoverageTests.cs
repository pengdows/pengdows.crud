#region

using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class NetworkConverterCoverageTests
{
    [Fact]
    public void MacAddressConverter_UsesStringForPostgres()
    {
        var converter = new MacAddressConverter();
        var mac = MacAddress.Parse("08:00:2b:01:02:03");
        var providerValue = converter.ToProviderValue(mac, SupportedDatabase.PostgreSql);

        Assert.Equal("08:00:2B:01:02:03", providerValue);
    }

    [Fact]
    public void MacAddressConverter_HandlesPhysicalAndProviderTypes()
    {
        var converter = new MacAddressConverter();
        var physical = PhysicalAddress.Parse("08002B010203");
        Assert.True(converter.TryConvertFromProvider(physical, SupportedDatabase.PostgreSql, out var parsed));
        Assert.Equal(new MacAddress(physical), parsed);

        var providerShim = new NpgsqlMacAddressShim(physical);
        Assert.True(
            converter.TryConvertFromProvider(providerShim, SupportedDatabase.PostgreSql, out var providerParsed));
        Assert.Equal(parsed, providerParsed);
    }

    [Fact]
    public void CidrConverter_ParsesTextAndProviderTypes()
    {
        var converter = new CidrConverter();
        Assert.True(converter.TryConvertFromProvider("192.168.10.0/24", SupportedDatabase.PostgreSql, out var cidr));
        Assert.Equal(24, cidr.PrefixLength);

        var providerShim = new NpgsqlCidrShim(IPAddress.Parse("10.0.0.0"), 16);
        Assert.True(converter.TryConvertFromProvider(providerShim, SupportedDatabase.PostgreSql, out var providerCidr));
        Assert.Equal(providerShim.PrefixLength, providerCidr.PrefixLength);
        Assert.Equal(providerShim.Address, providerCidr.Network);

        var providerValue = converter.ToProviderValue(cidr, SupportedDatabase.PostgreSql);
        Assert.Equal("192.168.10.0/24", providerValue);
    }

    [Fact]
    public void InetConverter_ParsesStringsAndIpAddresses()
    {
        var converter = new InetConverter();
        Assert.True(converter.TryConvertFromProvider("10.0.0.1/8", SupportedDatabase.PostgreSql, out var inet));
        Assert.Equal<int?>(8, inet.PrefixLength);

        var ip = IPAddress.Parse("2001:db8::1");
        Assert.True(converter.TryConvertFromProvider(ip, SupportedDatabase.PostgreSql, out var ipOnly));
        Assert.Equal(ip, ipOnly.Address);

        var providerShim = new NpgsqlInetShim(ip, 64);
        Assert.True(converter.TryConvertFromProvider(providerShim, SupportedDatabase.PostgreSql, out var providerInet));
        Assert.Equal(providerShim.PrefixLength, providerInet.PrefixLength);
        Assert.Equal(providerShim.Address, providerInet.Address);

        var providerValue = converter.ToProviderValue(inet, SupportedDatabase.PostgreSql);
        Assert.Equal("10.0.0.1/8", providerValue);
    }

    private sealed class NpgsqlMacAddressShim
    {
        public NpgsqlMacAddressShim(PhysicalAddress address)
        {
            Address = address;
        }

        public PhysicalAddress Address { get; }
    }

    private sealed class NpgsqlCidrShim
    {
        public NpgsqlCidrShim(IPAddress address, byte prefixLength)
        {
            Address = address;
            PrefixLength = prefixLength;
        }

        public IPAddress Address { get; }
        public byte PrefixLength { get; }
        public object Netmask => PrefixLength;
    }

    private sealed class NpgsqlInetShim
    {
        public NpgsqlInetShim(IPAddress address, byte prefixLength)
        {
            Address = address;
            PrefixLength = prefixLength;
        }

        public IPAddress Address { get; }
        public byte PrefixLength { get; }
        public object? Netmask => PrefixLength;
    }
}