using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
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

        var info = DataSourceInformation.Create(tracked, NullLoggerFactory.Instance);

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

        var info = DataSourceInformation.Create(tracked, NullLoggerFactory.Instance);

        Assert.Null(info.GetPostgreSqlMajorVersion());
    }

    private static bool InvokeIsSqliteSync(ITrackedConnection conn)
    {
        var method = typeof(DataSourceInformation).GetMethod("IsSqliteSync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { conn })!;
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
    public void IsSqliteSync_ReturnsFalse_WhenCommandFails()
    {
        var conn = new ThrowingConnection { ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}" };
        using var tracked = new TrackedConnection(conn);
        Assert.False(InvokeIsSqliteSync(tracked));
    }

    [Fact]
    public void IsSqliteSync_ReturnsTrue_WhenQuerySucceeds()
    {
        var conn = new FakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object> { { "version", "3" } } });
        using var tracked = new TrackedConnection(conn);
        Assert.True(InvokeIsSqliteSync(tracked));
    }
}
