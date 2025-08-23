using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.Tests.Mocks;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationNegativeTests
{
    [Fact]
    public void GetPostgreSqlMajorVersion_ReturnsMajor()
    {
        var info = DataSourceInformationTestHelper.CreatePostgreSqlInfo("15.2");
        Assert.Equal(15, info.GetPostgreSqlMajorVersion());
    }

    [Fact]
    public void GetPostgreSqlMajorVersion_NonPostgres_ReturnsNull()
    {
        var info = DataSourceInformationTestHelper.CreateMySqlInfo("8.0");
        Assert.Null(info.GetPostgreSqlMajorVersion());
    }


    [Fact]
    public void GetDatabaseVersion_ReturnsErrorMessage_WhenCommandFails()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var conn = new ThrowingConnection { ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}" };
        using var tracked = new TrackedConnection(conn);
        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);
        var result = info.GetDatabaseVersion(tracked);
        Assert.StartsWith("Error retrieving version", result);
    }

    private sealed class EmptyVersionDialect : SqlDialect
    {
        private readonly DatabaseProductInfo _info = new()
        {
            ProductName = "Test",
            ProductVersion = "1.0",
            ParsedVersion = new Version(1, 0),
            DatabaseType = SupportedDatabase.SqlServer,
            StandardCompliance = SqlStandardLevel.Sql92
        };

        public EmptyVersionDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger) { }
        public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;
        public override string ParameterMarker => "@";
        public override bool SupportsNamedParameters => true;
        public override int MaxParameterLimit => 1;
        public override int ParameterNameMaxLength => 1;
        public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;
        public override string GetVersionQuery() => string.Empty;
        public override Task<DatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection) => Task.FromResult(_info);
        public override DatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection) => _info;
    }

    [Fact]
    public void GetDatabaseVersion_ReturnsUnknown_WhenQueryEmpty()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new EmptyVersionDialect(factory, NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        using var tracked = new TrackedConnection(factory.CreateConnection());
        dialect.DetectDatabaseInfo(tracked);
        var info = new DataSourceInformation(dialect);
        var result = info.GetDatabaseVersion(tracked);
        Assert.Equal("Unknown Database Version", result);
    }
}
