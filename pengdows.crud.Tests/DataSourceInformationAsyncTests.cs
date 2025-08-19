using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationAsyncTests
{
    [Fact]
    public async Task CreateAsync_ReturnsInformation()
    {
        var schema = DataSourceInformation.BuildEmptySchema(
            "PostgreSQL", "15.2", "@p[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
        var scalars = new Dictionary<string, object> { ["SELECT version()"] = "PostgreSQL 15.2" };
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var conn = factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}";
        using var tracked = new FakeTrackedConnection(conn, schema, scalars);

        var info = await DataSourceInformation.CreateAsync(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal("PostgreSQL", info.DatabaseProductName);
    }

    private sealed class ThrowingConnection : FakeDbConnection
    {
        protected override DbCommand CreateDbCommand() => new ThrowingCommand(this);

        private sealed class ThrowingCommand : DbCommand
        {
            private readonly DbConnection _connection;
            public ThrowingCommand(DbConnection connection) => _connection = connection;
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
    public async Task CreateAsync_ReturnsErrorVersion_WhenCommandFails()
    {
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var conn = new ThrowingConnection { ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}" };
        using var tracked = new TrackedConnection(conn);

        var info = await DataSourceInformation.CreateAsync(tracked, factory, NullLoggerFactory.Instance);
        Assert.StartsWith("Error retrieving version", info.DatabaseProductVersion);
    }
}
