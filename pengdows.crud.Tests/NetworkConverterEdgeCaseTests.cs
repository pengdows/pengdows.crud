using System;
using System.Net;
using System.Net.NetworkInformation;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests edge cases for MacAddressConverter and CidrConverter.
/// </summary>
public class NetworkConverterEdgeCaseTests
{
    // ===== MacAddressConverter =====

    [Fact]
    public void MacAddressConverter_ConvertToProvider_PostgresReturnsString()
    {
        var converter = new MacAddressConverter();
        var mac = MacAddress.Parse("08:00:2B:01:02:03");

        var result = converter.ToProviderValue(mac, SupportedDatabase.PostgreSql);
        Assert.IsType<string>(result);
        Assert.Equal("08:00:2B:01:02:03", result);
    }

    [Fact]
    public void MacAddressConverter_ConvertToProvider_NonPostgres_ReturnsPhysicalAddress()
    {
        var converter = new MacAddressConverter();
        var mac = MacAddress.Parse("08:00:2B:01:02:03");

        var result = converter.ToProviderValue(mac, SupportedDatabase.Sqlite);
        Assert.IsType<PhysicalAddress>(result);
    }

    [Fact]
    public void MacAddressConverter_TryConvert_InvalidString_ReturnsFalse()
    {
        var converter = new MacAddressConverter();

        var success = converter.TryConvertFromProvider("not a mac", SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void MacAddressConverter_TryConvert_NpgsqlShim_NullAddress_ReturnsFalse()
    {
        var converter = new MacAddressConverter();
        var shim = new NpgsqlMacAddressShim(null);

        var success = converter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void MacAddressConverter_TryConvert_NpgsqlShim_ValidAddress_ReturnsTrue()
    {
        var converter = new MacAddressConverter();
        var physical = new PhysicalAddress(new byte[] { 0x08, 0x00, 0x2B, 0x01, 0x02, 0x03 });
        var shim = new NpgsqlMacAddressShim(physical);

        var success = converter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out var result);
        Assert.True(success);
        Assert.Equal(physical, result.Address);
    }

    [Fact]
    public void MacAddressConverter_TryConvert_UnknownType_ReturnsFalse()
    {
        var converter = new MacAddressConverter();

        var success = converter.TryConvertFromProvider(42, SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void MacAddressConverter_FromProviderValue_Null_ReturnsNull()
    {
        var converter = new MacAddressConverter();

        var result = converter.FromProviderValue(null!, SupportedDatabase.PostgreSql);
        Assert.Null(result);
    }

    [Fact]
    public void MacAddressConverter_TryConvert_PhysicalAddress_ReturnsTrue()
    {
        var converter = new MacAddressConverter();
        var physical = new PhysicalAddress(new byte[] { 0x08, 0x00, 0x2B, 0x01, 0x02, 0x03 });

        var success = converter.TryConvertFromProvider(physical, SupportedDatabase.PostgreSql, out var result);
        Assert.True(success);
        Assert.Equal(physical, result.Address);
    }

    // ===== CidrConverter =====

    [Fact]
    public void CidrConverter_ConvertToProvider_PostgresReturnString()
    {
        var converter = new CidrConverter();
        var cidr = Cidr.Parse("192.168.0.0/16");

        var result = converter.ToProviderValue(cidr, SupportedDatabase.PostgreSql);
        Assert.IsType<string>(result);
    }

    [Fact]
    public void CidrConverter_ConvertToProvider_CockroachDbReturnString()
    {
        var converter = new CidrConverter();
        var cidr = Cidr.Parse("10.0.0.0/8");

        var result = converter.ToProviderValue(cidr, SupportedDatabase.CockroachDb);
        Assert.IsType<string>(result);
    }

    [Fact]
    public void CidrConverter_ConvertToProvider_NonPostgresNonCockroach_ReturnsCidrStruct()
    {
        var converter = new CidrConverter();
        var cidr = Cidr.Parse("192.168.0.0/16");

        var result = converter.ToProviderValue(cidr, SupportedDatabase.Sqlite);
        Assert.IsType<Cidr>(result);
    }

    [Fact]
    public void CidrConverter_TryConvert_EmptyString_ReturnsFalse()
    {
        var converter = new CidrConverter();

        var success = converter.TryConvertFromProvider("", SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void CidrConverter_TryConvert_NoSlash_ReturnsFalse()
    {
        var converter = new CidrConverter();

        var success = converter.TryConvertFromProvider("192.168.0.0", SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void CidrConverter_TryConvert_NpgsqlShim_ValidValues_ReturnsTrue()
    {
        var converter = new CidrConverter();
        var shim = new NpgsqlCidrShim(IPAddress.Parse("10.0.0.0"), (byte)8);

        var success = converter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out var result);
        Assert.True(success);
        Assert.Equal("10.0.0.0/8", result.ToString());
    }

    [Fact]
    public void CidrConverter_TryConvert_NpgsqlShim_NullAddress_ReturnsFalse()
    {
        var converter = new CidrConverter();
        var shim = new NpgsqlCidrShim(null, (byte)8);

        var success = converter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void CidrConverter_TryConvert_NpgsqlShim_NullNetmask_ReturnsFalse()
    {
        var converter = new CidrConverter();
        var shim = new NpgsqlCidrShim(IPAddress.Parse("10.0.0.0"), null);

        var success = converter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void CidrConverter_TryConvert_UnknownType_ReturnsFalse()
    {
        var converter = new CidrConverter();

        var success = converter.TryConvertFromProvider(42, SupportedDatabase.PostgreSql, out _);
        Assert.False(success);
    }

    [Fact]
    public void CidrConverter_TryConvert_CidrPassthrough_ReturnsTrue()
    {
        var converter = new CidrConverter();
        var cidr = Cidr.Parse("192.168.0.0/16");

        var success = converter.TryConvertFromProvider(cidr, SupportedDatabase.PostgreSql, out var result);
        Assert.True(success);
        Assert.Equal(cidr, result);
    }

    // ===== Provider shim classes for reflection paths =====

    /// <summary>
    /// Simulates NpgsqlTypes.NpgsqlMacAddress for reflection-based conversion testing.
    /// The class name must contain "NpgsqlMacAddress" for the reflection check.
    /// </summary>
    private class NpgsqlMacAddressShim
    {
        public NpgsqlMacAddressShim(PhysicalAddress? address)
        {
            Address = address;
        }

        public PhysicalAddress? Address { get; }
    }

    /// <summary>
    /// Simulates NpgsqlTypes.NpgsqlCidr for reflection-based conversion testing.
    /// The class name must contain "NpgsqlCidr" for the reflection check.
    /// </summary>
    private class NpgsqlCidrShim
    {
        public NpgsqlCidrShim(IPAddress? address, object? netmask)
        {
            Address = address;
            Netmask = netmask;
        }

        public IPAddress? Address { get; }
        public object? Netmask { get; }
    }
}
