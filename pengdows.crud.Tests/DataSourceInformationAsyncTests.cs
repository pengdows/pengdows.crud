using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationAsyncTests
{
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
        var command = new Mock<DbCommand>();
        command.Setup(c => c.ExecuteReaderAsync(It.IsAny<CommandBehavior>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        command.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var conn = new Mock<ITrackedConnection>();
        conn.Setup(c => c.CreateCommand()).Returns(command.Object);

        var result = await InvokeIsSqliteAsync(conn.Object);

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
