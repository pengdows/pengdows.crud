using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlServerVersionParsingTests
{
    [Theory]
    [InlineData("Microsoft SQL Server 2019 (RTM) - 15.0.2000.5 (X64)", "15.0.2000.5")]
    [InlineData("Microsoft SQL Server 2012 (SP4-GDR) (KB4018073) - 11.0.7507.2 (X64)", "11.0.7507.2")]
    [InlineData("Microsoft SQL Server (unknown)", null)]
    public void ParseVersion_ExtractsProductVersion(string banner, string? expected)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance);

        var parsed = dialect.ParseVersion(banner);

        if (expected == null)
        {
            Assert.Null(parsed);
        }
        else
        {
            Assert.Equal(expected, parsed!.ToString());
        }
    }
}