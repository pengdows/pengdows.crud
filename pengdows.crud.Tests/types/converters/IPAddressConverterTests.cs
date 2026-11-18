using System.Net;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests.IpConverters
{
    public class IPAddressConverterTests
    {
        [Fact]
        public void TryConvertFromProvider_IPAddressValue_PassesThrough()
        {
            var address = IPAddress.Parse("192.168.1.10");

            Assert.True(new IPAddressConverter().TryConvertFromProvider(address, SupportedDatabase.PostgreSql, out var result));
            Assert.Equal(address, result);
        }
    }
}
