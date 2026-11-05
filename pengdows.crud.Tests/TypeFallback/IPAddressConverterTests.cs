using System;
using System.Data;
using System.Data.Common;
using System.Net;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// System.Net.IPAddress: a database with no address type takes its canonical text, and reads it back from text or from
/// the 4- or 16-byte form. The Npgsql-family databases bind the address value itself (Npgsql maps it to inet).
/// </summary>
public class IPAddressConverterTests
{
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Oracle)]
    public void AddressWithoutANativeType_IsBoundAsItsText(SupportedDatabase provider)
    {
        var parameter = new fakeDbFactory(provider).CreateParameter();
        var registry = new AdvancedTypeRegistry(true);

        Assert.True(registry.TryConfigureParameter(parameter, typeof(IPAddress),
            IPAddress.Parse("192.168.1.10"), provider));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("192.168.1.10", parameter.Value);
    }

    [Fact]
    public void AddressOnNpgsqlFamily_IsBoundAsTheAddressValue()
    {
        var converter = new IPAddressConverter();
        var address = IPAddress.Parse("192.168.1.10");

        var bound = converter.ToProviderValue(address, SupportedDatabase.PostgreSql);

        Assert.Same(address, bound);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("2001:db8::1")]
    public void TextRoundTrips(string text)
    {
        var converter = new IPAddressConverter();

        Assert.True(converter.TryConvertFromProvider(text, SupportedDatabase.SqlServer, out var address));
        Assert.Equal(IPAddress.Parse(text), address);
    }

    [Fact]
    public void FourAndSixteenByteForms_ReadAsAddresses()
    {
        var converter = new IPAddressConverter();
        var v4 = IPAddress.Parse("10.0.0.1");
        var v6 = IPAddress.Parse("2001:db8::1");

        Assert.True(converter.TryConvertFromProvider(v4.GetAddressBytes(), SupportedDatabase.MySql, out var r4));
        Assert.Equal(v4, r4);
        Assert.True(converter.TryConvertFromProvider(v6.GetAddressBytes(), SupportedDatabase.MySql, out var r6));
        Assert.Equal(v6, r6);
    }

    [Fact]
    public void UnparseableText_IsNotAnAddress()
    {
        var converter = new IPAddressConverter();

        Assert.False(converter.TryConvertFromProvider("not an address", SupportedDatabase.SqlServer, out _));
    }
}

/// <summary>
/// The dialect's parameter path (the one every statement takes) reaches a converter-only type too: an address with no
/// registered mapping on SQL Server or MySQL is bound as its text, not refused by the provider.
/// </summary>
public class IPAddressDialectParameterTests
{
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    public void DialectCreatesTheAddressParameterAsText(SupportedDatabase provider)
    {
        var dialect = pengdows.crud.dialects.SqlDialectFactory.CreateDialectForType(
            provider, new fakeDbFactory(provider), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, IPAddress.Parse("192.168.1.10"));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("192.168.1.10", parameter.Value);
    }
}

/// <summary>
/// On the Npgsql family the converter hands the address value to the driver, which maps it to inet. The fallback must
/// not turn that value into text: a text parameter does not bind to an inet column.
/// </summary>
public class IPAddressNpgsqlParameterTests
{
    [Fact]
    public void PostgreSqlBindsTheAddressValueNotItsText()
    {
        var parameter = new fakeDbFactory(SupportedDatabase.PostgreSql).CreateParameter();
        var registry = new AdvancedTypeRegistry(true);
        var address = IPAddress.Parse("192.168.1.10");

        Assert.True(registry.TryConfigureParameter(parameter, typeof(IPAddress), address, SupportedDatabase.PostgreSql));

        Assert.Same(address, parameter.Value);
        Assert.NotEqual(DbType.String, parameter.DbType);
    }
}
