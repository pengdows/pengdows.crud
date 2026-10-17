using System.Net;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class CidrFromProviderTests
{
    [Fact]
    public void FromProvider_KeepsTheProviderAddressWithoutCanonicalizingAgain()
    {
        var address = IPAddress.Parse("10.1.0.0");

        var cidr = Cidr.FromProvider(address, 16);

        Assert.Same(address, cidr.Network);
        Assert.Equal((byte)16, cidr.PrefixLength);
    }

    [Theory]
    [InlineData("10.1.0.0", 16)]
    [InlineData("192.168.1.128", 25)]
    [InlineData("2001:db8::", 32)]
    public void FromProvider_EqualsTheValidatingConstructorForACanonicalValue(string text, byte prefix)
    {
        var address = IPAddress.Parse(text);

        Assert.Equal(new Cidr(address, prefix), Cidr.FromProvider(address, prefix));
    }
}
