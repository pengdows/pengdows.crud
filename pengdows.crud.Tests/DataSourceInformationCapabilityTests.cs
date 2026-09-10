using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationCapabilityTests
{
    [Fact]
    public void FallbackDialect_ReportsLimitedCapabilities()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Unknown Database (SQL-92 Compatible)",
            "Unknown Version",
            "\\?",
            "{0}",
            18,
            "[a-zA-Z][a-zA-Z0-9_]*",
            "[a-zA-Z][a-zA-Z0-9_]*",
            false);
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}";
        var tracked = new FakeTrackedConnection(conn, schema, new Dictionary<string, object>());

        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);

        Assert.True(info.IsUsingFallbackDialect);
        Assert.False(string.IsNullOrEmpty(info.GetCompatibilityWarning()));
        Assert.False(info.CanUseModernFeatures);
        Assert.True(info.HasBasicCompatibility);
    }

    [Fact]
    public void KnownDialect_DoesNotUseFallback()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "Microsoft SQL Server",
            "15.0",
            "@[0-9]+",
            "@{0}",
            64,
            "@\\w+",
            "@\\w+",
            true);
        var scalars = new Dictionary<string, object>
        {
            ["SELECT @@VERSION"] = "Microsoft SQL Server 15.0"
        };
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}";
        var tracked = new FakeTrackedConnection(conn, schema, scalars);

        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);

        Assert.False(info.IsUsingFallbackDialect);
        Assert.Equal(string.Empty, info.GetCompatibilityWarning());
        Assert.True(info.CanUseModernFeatures);
        Assert.True(info.HasBasicCompatibility);
    }
}