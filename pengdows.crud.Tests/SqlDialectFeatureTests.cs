using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectFeatureTests
{
    [Fact]
    public async Task SupportsJsonTypes_SqlServer2019_ReturnsTrue()
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
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLogger<SqlDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(tracked);
        Assert.True(dialect.SupportsJsonTypes);
    }

    [Fact]
    public async Task SupportsJsonTypes_OldSqlServer_ReturnsFalse()
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
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLogger<SqlDialect>.Instance);
        await dialect.DetectDatabaseInfoAsync(tracked);
        Assert.False(dialect.SupportsJsonTypes);
    }
}