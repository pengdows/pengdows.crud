using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlServerMergeSupportTests
{
    [Theory]
    [InlineData("SQL Server 9.0", false)]
    [InlineData("SQL Server 10.0", true)]
    public void SupportsMerge_Depends_On_Version(string versionString, bool expected)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer.ToString());
        var conn = factory.CreateConnection();
        conn.ConnectionString = "Data Source=test;EmulatedProduct=SqlServer";
        var versionSql = "SELECT @@VERSION";
        var scalars = new Dictionary<string, object>
        {
            { versionSql, versionString }
        };
        var schema = DataSourceInformation.BuildEmptySchema(
            "SQL Server",
            "9.0",
            "@p[0-9]+",
            "@{0}",
            128,
            @"@\\w+",
            @"@\\w+",
            true);
        var tracked = new FakeTrackedConnection(conn, schema, scalars);
        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(expected, info.SupportsMerge);
        Assert.NotEqual(!expected, info.SupportsMerge);
    }
}