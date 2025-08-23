using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;

namespace pengdows.crud.Tests.Mocks;

public static class DataSourceInformationTestHelper
{
    public static DataSourceInformation CreatePostgreSqlInfo(string version = "15.2")
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "PostgreSQL", version, "@p[0-9]+", ":{0}", 64, "@\\w+", "[@:]\\w+", true);
        var scalars = new Dictionary<string, object> { ["SELECT version()"] = $"PostgreSQL {version}" };
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}";
        using var tracked = new FakeTrackedConnection(conn, schema, scalars);

        return DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);
    }

    public static DataSourceInformation CreateMySqlInfo(string version = "8.0")
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "MySQL", version, "@[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
        var scalars = new Dictionary<string, object> { ["SELECT VERSION()"] = version };
        var factory = new FakeDbFactory(SupportedDatabase.MySql);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}";
        using var tracked = new FakeTrackedConnection(conn, schema, scalars);

        return DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);
    }

    public static DataSourceInformation CreateSqlServerInfo(string version = "15.0")
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server", version, "@[0-9]+", "@{0}", 128, "@\\w+", "@\\w+", true);
        var scalars = new Dictionary<string, object> { ["SELECT @@VERSION"] = $"Microsoft SQL Server {version}" };
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        using var tracked = new FakeTrackedConnection(conn, schema, scalars);

        return DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);
    }

    public static (FakeTrackedConnection TrackedConnection, FakeDbFactory Factory) CreateTestConnection(
        SupportedDatabase database, 
        string productName, 
        string version,
        string parameterPattern = "@[0-9]+",
        string parameterFormat = "@{0}",
        int maxLength = 64,
        string namePattern = "@\\w+",
        string namePatternRegex = "@\\w+",
        bool supportsNamed = true,
        Dictionary<string, object>? additionalScalars = null)
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            productName, version, parameterPattern, parameterFormat, maxLength, namePattern, namePatternRegex, supportsNamed);
        var scalars = additionalScalars ?? new Dictionary<string, object>();
        var factory = new FakeDbFactory(database);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={database}";
        var tracked = new FakeTrackedConnection(conn, schema, scalars);

        return (tracked, factory);
    }
}