using System.Data;
using System.Net.NetworkInformation;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A MAC address is bound as the driver's PhysicalAddress only where the driver takes it (the Npgsql family). Every other
/// database gets its canonical text: a PhysicalAddress has no bind on SQL Server, MySQL or the rest, and would be refused.
/// </summary>
public class MacAddressProviderTests
{
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void OffTheNpgsqlFamilyTheAddressIsItsText(SupportedDatabase provider)
    {
        var mac = MacAddress.Parse("08:00:2b:01:02:03");

        var bound = new MacAddressConverter().ToProviderValue(mac, provider);

        Assert.Equal("08:00:2B:01:02:03", bound);
    }

    [Fact]
    public void OnPostgreSqlTheAddressIsThePhysicalAddress()
    {
        var mac = MacAddress.Parse("08:00:2b:01:02:03");

        var bound = new MacAddressConverter().ToProviderValue(mac, SupportedDatabase.PostgreSql);

        Assert.IsType<PhysicalAddress>(bound);
    }

    [Fact]
    public void OnSqlServerTheParameterIsText()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.SqlServer,
            new fakeDbFactory(SupportedDatabase.SqlServer), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, MacAddress.Parse("08:00:2b:01:02:03"));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("08:00:2B:01:02:03", parameter.Value);
    }
}
