#region

using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class NetworkConverterBehaviorTests
{
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

    // TYPE-002 (found live): Npgsql reports a host address stored without a prefix as netmask 32
    // (/128 for IPv6). PostgreSQL's own text form omits a full-length mask, so it reads as no
    // prefix, equal to the Inet that was written.
    [Theory]
    [InlineData("192.168.1.20", (byte)32)]
    [InlineData("2001:db8::1", (byte)128)]
    public void InetConverter_FullLengthNetmask_ReadsAsNoPrefix(string address, byte netmask)
    {
        var shim = new NpgsqlInetShim(IPAddress.Parse(address), netmask);

        Assert.True(new InetConverter().TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out var inet));

        Assert.Null(inet.PrefixLength);
        Assert.Equal(Inet.Parse(address), inet);
    }

    [Theory]
    [InlineData("192.168.1.20", (byte)32)]
    [InlineData("2001:db8::1", (byte)128)]
    public void InetCoercion_FullLengthNetmask_ReadsAsNoPrefix(string address, byte netmask)
    {
        var shim = new NpgsqlInetShim(IPAddress.Parse(address), netmask);

        Assert.True(new pengdows.crud.types.coercion.InetCoercion().TryRead(
            new pengdows.crud.types.coercion.DbValue(shim), out var inet));

        Assert.Null(inet.PrefixLength);
        Assert.Equal(Inet.Parse(address), inet);
    }

    [Fact]
    public void InetCoercion_ShorterNetmask_KeepsThePrefix()
    {
        var shim = new NpgsqlInetShim(IPAddress.Parse("192.168.1.10"), 24);

        Assert.True(new pengdows.crud.types.coercion.InetCoercion().TryRead(
            new pengdows.crud.types.coercion.DbValue(shim), out var inet));

        Assert.Equal<int?>(24, inet.PrefixLength);
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
