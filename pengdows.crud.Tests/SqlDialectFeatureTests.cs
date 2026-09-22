using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

    [Fact]
    public async Task SupportsXmlUdtTemporal_SqlServer2019_ReturnTrue()
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

        Assert.True(dialect.SupportsXmlTypes);
        Assert.True(dialect.SupportsUserDefinedTypes);
        Assert.True(dialect.SupportsTemporalData);
    }

    [Fact]
    public async Task SupportsXmlUdt_SqlServer2000_ReturnFalse_TemporalStillFalse()
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

        // SQL Server 2000 (major 8) predates the xml type and CLR/T-SQL UDTs (both 2005/major 9).
        Assert.False(dialect.SupportsXmlTypes);
        Assert.False(dialect.SupportsUserDefinedTypes);
        Assert.False(dialect.SupportsTemporalData);
    }

    [Fact]
    public void SupportsTruncateTable_Sqlite_ReturnsFalse()
    {
        // SQLite has no TRUNCATE TABLE statement at all; DELETE FROM is the only option.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Sqlite,
            factory,
            NullLogger<SqlDialect>.Instance);

        Assert.False(dialect.SupportsTruncateTable);
    }

    [Fact]
    public void SupportsTruncateTable_Access_ReturnsFalse()
    {
        // Jet/ACE SQL has no TRUNCATE TABLE statement.
        var factory = new fakeDbFactory(SupportedDatabase.Access);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Access,
            factory,
            NullLogger<SqlDialect>.Instance);

        Assert.False(dialect.SupportsTruncateTable);
    }
}