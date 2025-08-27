using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.Tests.Mocks;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectCapabilityTests
{
    [Fact]
    public void IsFallbackDialect_ReturnsTrueForUnknown()
    {
        var scalars = new Dictionary<string, object>
        {
            ["SELECT 'SQL-92 Compatible Database' AS version"] = "SQL-92 Compatible Database"
        };
        var (tracked, factory) = DataSourceInformationTestHelper.CreateTestConnection(
            SupportedDatabase.Unknown,
            "MysteryDB",
            "1.0",
            parameterPattern: "?",
            parameterFormat: "?{0}",
            maxLength: 18,
            namePattern: "?",
            namePatternRegex: "?",
            supportsNamed: false,
            additionalScalars: scalars);
        var dialect = new Sql92Dialect(factory, NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        dialect.DetectDatabaseInfo(tracked);
        Assert.True(dialect.IsFallbackDialect);
        Assert.Equal(
            "Using SQL-92 fallback dialect - some features may be unavailable",
            dialect.GetCompatibilityWarning());
    }

    [Fact]
    public void IsFallbackDialect_ReturnsFalseForKnown()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server",
            "15.0",
            "@[0-9]+",
            "@{0}",
            128,
            "@\\w+",
            "@\\w+",
            true);
        var scalars = new Dictionary<string, object> { ["SELECT @@VERSION"] = "Microsoft SQL Server 15.0" };
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var tracked = new FakeTrackedConnection(conn, schema, scalars);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        dialect.DetectDatabaseInfo(tracked);
        Assert.False(dialect.IsFallbackDialect);
        Assert.Equal(string.Empty, dialect.GetCompatibilityWarning());
    }

    [Fact]
    public void CanUseModernFeatures_FollowsStandardCompliance()
    {
        var modernSchema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server",
            "15.0",
            "@[0-9]+",
            "@{0}",
            128,
            "@\\w+",
            "@\\w+",
            true);
        var modernScalars = new Dictionary<string, object> { ["SELECT @@VERSION"] = "Microsoft SQL Server 15.0" };
        var modernFactory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var modernConn = modernFactory.CreateConnection();
        modernConn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var modernTracked = new FakeTrackedConnection(modernConn, modernSchema, modernScalars);
        var modernDialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            modernFactory,
            NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        modernDialect.DetectDatabaseInfo(modernTracked);

        var legacySchema = DataSourceInformation.BuildEmptySchema(
            "MySQL",
            "5.0",
            "@[0-9]+",
            "@{0}",
            64,
            "@\\w+",
            "@\\w+",
            true);
        var legacyScalars = new Dictionary<string, object> { ["SELECT VERSION()"] = "5.0" };
        var legacyFactory = new FakeDbFactory(SupportedDatabase.MySql);
        var legacyConn = legacyFactory.CreateConnection();
        legacyConn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}";
        var legacyTracked = new FakeTrackedConnection(legacyConn, legacySchema, legacyScalars);
        var legacyDialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.MySql,
            legacyFactory,
            NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        legacyDialect.DetectDatabaseInfo(legacyTracked);

        Assert.True(modernDialect.CanUseModernFeatures);
        Assert.False(legacyDialect.CanUseModernFeatures);
    }

    private sealed class Sql89Dialect : Sql92Dialect
    {
        public Sql89Dialect(DbProviderFactory factory, ILogger logger) : base(factory, logger) { }
        protected override SqlStandardLevel DetermineStandardCompliance(Version? version) => SqlStandardLevel.Sql89;
    }

    [Fact]
    public void HasBasicCompatibility_FollowsStandardCompliance()
    {
        var compliantSchema = DataSourceInformation.BuildEmptySchema(
            "MySQL",
            "5.0",
            "@[0-9]+",
            "@{0}",
            64,
            "@\\w+",
            "@\\w+",
            true);
        var compliantScalars = new Dictionary<string, object> { ["SELECT VERSION()"] = "5.0" };
        var compliantFactory = new FakeDbFactory(SupportedDatabase.MySql);
        var compliantConn = compliantFactory.CreateConnection();
        compliantConn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}";
        var compliantTracked = new FakeTrackedConnection(compliantConn, compliantSchema, compliantScalars);
        var compliantDialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.MySql,
            compliantFactory,
            NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        compliantDialect.DetectDatabaseInfo(compliantTracked);

        var scalars = new Dictionary<string, object>
        {
            ["SELECT 'SQL-92 Compatible Database' AS version"] = "SQL-92 Compatible Database"
        };
        var factory = new FakeDbFactory(SupportedDatabase.Unknown);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}";
        var tracked = new FakeTrackedConnection(
            conn,
            DataSourceInformation.BuildEmptySchema(
                "LegacyDB",
                "0.1",
                "?",
                "?{0}",
                18,
                "?",
                "?",
                false),
            scalars);
        var nonDialect = new Sql89Dialect(factory, NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        nonDialect.DetectDatabaseInfo(tracked);

        Assert.True(compliantDialect.HasBasicCompatibility);
        Assert.False(nonDialect.HasBasicCompatibility);
    }
}
