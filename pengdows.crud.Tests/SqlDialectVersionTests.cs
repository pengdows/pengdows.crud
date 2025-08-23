using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectVersionTests
{
    [Fact]
    public void GetDatabaseVersion_ReturnsVersion()
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
        var result = dialect.GetDatabaseVersion(tracked);
        Assert.Contains("Microsoft SQL Server 15.0", result);
    }

    private sealed class ThrowingConnection : FakeDbConnection
    {
        protected override DbCommand CreateDbCommand() => new ThrowingCommand(this);
    }

    private sealed class ThrowingCommand : FakeDbCommand
    {
        public ThrowingCommand(DbConnection connection) : base(connection) { }
        public override object ExecuteScalar() => throw new InvalidOperationException("fail");
    }

    [Fact]
    public void GetDatabaseVersion_CommandFails_ReturnsErrorMessage()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var conn = new ThrowingConnection { ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}" };
        using var tracked = new TrackedConnection(conn);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.SqlServer,
            factory,
            NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        var result = dialect.GetDatabaseVersion(tracked);
        Assert.StartsWith("Error retrieving version", result);
    }

    private sealed class EmptyVersionDialect : SqlDialect
    {
        private readonly IDatabaseProductInfo _info = new DatabaseProductInfo
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
        public override Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection) => Task.FromResult(_info);
        public override IDatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection) => _info;
    }

    [Fact]
    public void GetDatabaseVersion_EmptyQuery_ReturnsUnknown()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new EmptyVersionDialect(factory, NullLoggerFactory.Instance.CreateLogger<SqlDialect>());
        using var tracked = new TrackedConnection(factory.CreateConnection());
        var result = dialect.GetDatabaseVersion(tracked);
        Assert.Equal("Unknown Database Version", result);
    }
}
