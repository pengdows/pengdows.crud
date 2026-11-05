using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// CockroachDB has no cidr type (its address type is inet, which holds the network too). A CIDR is therefore bound as its
/// text, not as an NpgsqlCidr, which the server refuses ("NpgsqlDbType 'Cidr' isn't present in your database"). The unit
/// environment has no Npgsql, so the live fidelity cell is the check that refused it.
/// </summary>
public class CidrCockroachTests
{
    [Fact]
    public void OnCockroachDbTheNetworkIsBoundAsItsText()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.CockroachDb,
            new fakeDbFactory(SupportedDatabase.CockroachDb), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, Cidr.Parse("10.0.0.0/8"));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("10.0.0.0/8", parameter.Value);
    }
}
