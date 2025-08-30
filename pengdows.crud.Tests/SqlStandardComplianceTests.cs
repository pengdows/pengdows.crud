using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlStandardComplianceTests
{
    [Theory]
    [InlineData("Microsoft SQL Server 2019 (RTM-CU20) (KB5007262) - 15.0.4236.7 (X64)", SqlStandardLevel.Sql2016, "15.0")]
    [InlineData("Microsoft SQL Server 2012 (SP4-GDR) (KB4018073) - 11.0.7507.2 (X64)", SqlStandardLevel.Sql2008, "11.0")]
    [InlineData("Microsoft SQL Server (unknown)", SqlStandardLevel.Sql2008, "0.0")]
    public async Task MaxSupportedStandard_SqlServer_VariesByVersion(string banner, SqlStandardLevel expected, string schemaVersion)
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server",
            schemaVersion,
            "@[0-9]+",
            "@{0}",
            64,
            @"@\w+",
            @"@\w+",
            true);
        var scalars = new Dictionary<string, object>
        {
            ["SELECT @@VERSION"] = banner
        };
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var tracked = new FakeTrackedConnection(conn, schema, scalars);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLogger<SqlDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(tracked);
        Assert.Equal(expected, dialect.MaxSupportedStandard);
    }
}
