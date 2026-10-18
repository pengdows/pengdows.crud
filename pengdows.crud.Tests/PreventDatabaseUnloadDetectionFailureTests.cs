using System;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using pengdows.crud.Tests.Logging;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29 (carried over from KeepAliveConnectionStrategy): HandleDialectDetection turned
/// any detection failure into (null, null) with an empty catch, so a database that could not be
/// detected was indistinguishable from one that was never asked. The fallback stays; the failure is
/// now logged.
/// </summary>
public sealed class PreventDatabaseUnloadDetectionFailureTests
{
    [Fact]
    public void HandleDialectDetection_OpenFailure_ReturnsNullsAndLogsTheFailure()
    {
        var logs = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { logs });
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context = new DatabaseContext(new pengdows.crud.configuration.DatabaseContextConfiguration { ConnectionString = "Data Source=:memory:" }, factory, loggerFactory);
        var strategy = new PreventDatabaseUnloadConnectionStrategy(context);
        var connection = new Mock<ITrackedConnection>();
        connection.SetupGet(c => c.State).Returns(ConnectionState.Closed);
        connection.Setup(c => c.Open()).Throws(new InvalidOperationException("detection open failed"));

        var (dialect, dataSourceInfo) = strategy.HandleDialectDetection(connection.Object, factory,
            NullLoggerFactory.Instance);

        Assert.Null(dialect);
        Assert.Null(dataSourceInfo);
        Assert.Contains(logs.Entries, e => e.Level >= LogLevel.Warning && e.Exception?.Message == "detection open failed");
    }
}
