using System.Net;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A CIDR read from a column that the database types as inet (CockroachDB) arrives as an Inet with its prefix; the CIDR
/// keeps that prefix. Read as a bare address it would come back as a /32 host, which is a different network.
/// </summary>
public class CidrFromInetTests
{
    [Fact]
    public void AnInetWithAPrefixIsTheCidrOfThatNetwork()
    {
        var inet = new Inet(IPAddress.Parse("10.0.0.0"), 8);

        Assert.True(new CidrConverter().TryConvertFromProvider(inet, SupportedDatabase.CockroachDb, out var cidr));

        Assert.Equal(Cidr.Parse("10.0.0.0/8"), cidr);
    }
}
