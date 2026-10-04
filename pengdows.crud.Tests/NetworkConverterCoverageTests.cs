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

public class NetworkConverterCoverageTests
{
    [Fact]
    public void MacAddressConverter_UsesPhysicalAddressForPostgres()
    {
        // PostgreSQL now gets the underlying PhysicalAddress like every other provider: Npgsql
        // rejects a string with NpgsqlDbType.MacAddr (confirmed live, BP-112).
        var converter = new MacAddressConverter();
        var mac = MacAddress.Parse("08:00:2b:01:02:03");
        var providerValue = converter.ToProviderValue(mac, SupportedDatabase.PostgreSql);

        var physical = Assert.IsType<PhysicalAddress>(providerValue);
        Assert.Equal(mac.Address, physical);
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