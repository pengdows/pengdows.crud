using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlStandardLevelTests
{
    [Fact]
    public void StandardCompliance_SqlServer2019_ReturnsSql2016()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server",
            "15.0",
            "@[0-9]+",
            "@{0}",
            64,
            @"@\w+",
            @"@\w+",
            true);
        var scalars = new Dictionary<string, object>
        {
            ["SELECT @@VERSION"] = "Microsoft SQL Server 15.0"
        };
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var tracked = new FakeTrackedConnection(conn, schema, scalars);
        var info = DataSourceInformation.Create(tracked, factory);
        Assert.Equal(SqlStandardLevel.Sql2016, info.StandardCompliance);
    }

    [Fact]
    public void StandardCompliance_OldSqlServer_ReturnsSql2003()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server",
            "8.0",
            "@[0-9]+",
            "@{0}",
            64,
            @"@\w+",
            @"@\w+",
            true);
        var scalars = new Dictionary<string, object>
        {
            ["SELECT @@VERSION"] = "Microsoft SQL Server 8.0"
        };
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var tracked = new FakeTrackedConnection(conn, schema, scalars);
        var info = DataSourceInformation.Create(tracked, factory);
        Assert.Equal(SqlStandardLevel.Sql2003, info.StandardCompliance);
    }

    [Fact]
    public void StandardCompliance_VeryOldSqlServer_UsesDefault()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server",
            "7.0",
            "@[0-9]+",
            "@{0}",
            64,
            @"@\w+",
            @"@\w+",
            true);
        var scalars = new Dictionary<string, object>
        {
            ["SELECT @@VERSION"] = "Microsoft SQL Server 7.0"
        };
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var tracked = new FakeTrackedConnection(conn, schema, scalars);
        var info = DataSourceInformation.Create(tracked, factory);
        Assert.Equal(SqlStandardLevel.Sql2008, info.StandardCompliance);
    }
}