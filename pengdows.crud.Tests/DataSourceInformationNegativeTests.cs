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
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationNegativeTests
{
    [Fact]
    public void GetPostgreSqlMajorVersion_ReturnsMajor()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "PostgreSQL", "15.2", "@p[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
        var scalars = new Dictionary<string, object> { ["SELECT version()"] = "PostgreSQL 15.2" };
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}";
        using var tracked = new FakeTrackedConnection(conn, schema, scalars);

        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);

        Assert.Equal(15, info.GetPostgreSqlMajorVersion());
    }

    [Fact]
    public void GetPostgreSqlMajorVersion_NonPostgres_ReturnsNull()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "MySQL", "8.0", "@[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
        var scalars = new Dictionary<string, object> { ["SELECT VERSION()"] = "8.0" };
        var factory = new FakeDbFactory(SupportedDatabase.MySql);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}";
        using var tracked = new FakeTrackedConnection(conn, schema, scalars);

        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);

        Assert.Null(info.GetPostgreSqlMajorVersion());
    }

    private sealed class ThrowingConnection : FakeDbConnection
    {
        protected override DbCommand CreateDbCommand() => new ThrowingCommand(this);

        private sealed class ThrowingCommand : DbCommand
        {
            private readonly DbConnection _connection;
            public ThrowingCommand(DbConnection c) => _connection = c;
            public override string? CommandText { get; set; }
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection DbConnection { get => _connection; set { } }
            protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
            protected override DbTransaction? DbTransaction { get; set; }
            public override void Cancel() { }
            public override int ExecuteNonQuery() => throw new InvalidOperationException();
            public override object? ExecuteScalar() => throw new InvalidOperationException();
            public override void Prepare() { }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new InvalidOperationException();
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => throw new InvalidOperationException();
            protected override DbParameter CreateDbParameter() => new FakeDbParameter();
        }
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
