using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationAsyncTests
{
    private sealed class ThrowingCommand : DbCommand
    {
        private readonly DbConnection _connection;

        public ThrowingCommand(DbConnection connection)
        {
            _connection = connection;
        }

        public override string? CommandText { get; set; }
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new InvalidOperationException();
        public override object? ExecuteScalar() => throw new InvalidOperationException();
        public override void Prepare() { }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new InvalidOperationException();

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => throw new InvalidOperationException();

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
    }

    private sealed class ThrowingConnection : FakeDbConnection
    {
        protected override DbCommand CreateDbCommand() => new ThrowingCommand(this);
    }
    private static async Task<bool> InvokeIsSqliteAsync(ITrackedConnection conn)
    {
        var method = typeof(DataSourceInformation).GetMethod("IsSqliteAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<bool>)method.Invoke(null, new object?[] { conn, NullLogger<DataSourceInformation>.Instance })!;
        return await task.ConfigureAwait(false);
    }

    private static async Task<string> InvokeGetVersionAsync(ITrackedConnection conn, string sql)
    {
        var method = typeof(DataSourceInformation).GetMethod("GetVersionAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<string>)method.Invoke(null, new object?[] { conn, sql, NullLogger<DataSourceInformation>.Instance })!;
        return await task.ConfigureAwait(false);
    }

    [Fact]
    public async Task IsSqliteAsync_ReturnsTrue_WhenVersionQuerySucceeds()
    {
        var conn = new FakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object> { { "version", "3.0" } } });
        using var tracked = new TrackedConnection(conn);

        var result = await InvokeIsSqliteAsync(tracked);

        Assert.True(result);
    }

    [Fact]
    public async Task IsSqliteAsync_ReturnsFalse_WhenCommandFails()
    {
        var conn = new ThrowingConnection
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}"
        };
        using var tracked = new TrackedConnection(conn);

        var result = await InvokeIsSqliteAsync(tracked);

        Assert.False(result);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsValue()
    {
        var conn = new FakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object> { { "version", "9.9" } } });
        using var tracked = new TrackedConnection(conn);

        var result = await InvokeGetVersionAsync(tracked, "SELECT sqlite_version()");

        Assert.Equal("9.9", result);
    }
}
