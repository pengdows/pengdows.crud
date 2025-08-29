using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.Tests.Mocks;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationSchemaTests
{
    [Fact]
    public void GetSchema_ReturnsProviderSchema_WhenAvailable()
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
        var info = new DataSourceInformation(dialect);

        var schema = info.GetSchema(tracked);

        Assert.Equal("MysteryDB", schema.Rows[0].Field<string>("DataSourceProductName"));
    }

    private sealed class ThrowingTrackedConnection : FakeTrackedConnection, ITrackedConnection
    {
        public ThrowingTrackedConnection(DbConnection connection)
            : base(connection, new DataTable(), new Dictionary<string, object>())
        {
        }

        DataTable ITrackedConnection.GetSchema(string dataSourceInformation)
        {
            throw new InvalidOperationException("schema not supported");
        }

        DataTable ITrackedConnection.GetSchema()
        {
            throw new InvalidOperationException("schema not supported");
        }
    }

    [Fact]
    public void GetSchema_ReturnsFallback_WhenProviderSchemaUnavailable()
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
        var info = new DataSourceInformation(dialect);

        var failingConn = new ThrowingTrackedConnection((DbConnection)factory.CreateConnection());
        failingConn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}";
        failingConn.Open();

        var schema = info.GetSchema(failingConn);

        Assert.Equal("Unknown Database (SQL-92 Compatible)", schema.Rows[0].Field<string>("DataSourceProductName"));
        Assert.True(schema.Rows[0].Field<bool>("SupportsNamedParameters"));
        Assert.NotEqual(false, schema.Rows[0].Field<bool>("SupportsNamedParameters"));
    }
}
