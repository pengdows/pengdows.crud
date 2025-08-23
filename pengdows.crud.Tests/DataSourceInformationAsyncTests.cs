using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.Tests.Mocks;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationAsyncTests
{
    [Fact]
    public async Task CreateAsync_ReturnsInformation()
    {
        var scalars = new Dictionary<string, object> { ["SELECT version()"] = "PostgreSQL 15.2" };
        var (tracked, factory) = DataSourceInformationTestHelper.CreateTestConnection(
            SupportedDatabase.PostgreSql, "PostgreSQL", "15.2", 
            "@p[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true, scalars);

        using (tracked)
        {
            var info = await DataSourceInformation.CreateAsync(tracked, factory, NullLoggerFactory.Instance);
            Assert.Equal("PostgreSQL", info.DatabaseProductName);
        }
    }


    [Fact]
    public async Task CreateAsync_ReturnsErrorVersion_WhenCommandFails()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var conn = new ThrowingConnection { ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}" };
        using var tracked = new TrackedConnection(conn);

        var info = await DataSourceInformation.CreateAsync(tracked, factory, NullLoggerFactory.Instance);
        Assert.StartsWith("Error retrieving version", info.DatabaseProductVersion);
    }
}
