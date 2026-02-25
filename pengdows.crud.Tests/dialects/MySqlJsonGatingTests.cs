using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class MySqlJsonGatingTests
{
    [Theory]
    [InlineData("5.5.0", false)]
    [InlineData("5.6.10", false)]
    [InlineData("5.7.8", true)]
    [InlineData("8.0.21", true)]
    public void SupportsJsonTypes_CorrectlyGatedByVersion(string version, bool expectedSupport)
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);
        
        // Mock the product info since MySqlDialect uses IsInitialized && ProductInfo
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.ScalarResultsByCommand["SELECT VERSION()"] = version;
        
        using var tracked = new FakeTrackedConnection(connection, new DataTable(), new System.Collections.Generic.Dictionary<string, object>
        {
            ["SELECT VERSION()"] = version
        });
        
        dialect.DetectDatabaseInfo(tracked);
        
        Assert.Equal(expectedSupport, dialect.SupportsJsonTypes);
    }
}
