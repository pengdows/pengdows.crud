using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A <c>MetricsUpdated</c> subscriber must never be able to turn an already-successful database
/// command into an apparent failure. Before the fix this guards, an exception thrown by a
/// subscriber propagated synchronously out of <c>CommandSucceeded</c>/<c>NotifyUpdated</c>/
/// <c>OnMetricsCollectorUpdated</c>, straight out of <c>ExecuteNonQueryAsync</c> — even though the
/// provider had already executed the command. Worse, the surrounding exception-translation catch
/// block then called <c>CommandFailed</c>, corrupting the metrics for a command that had, in
/// fact, succeeded.
/// </summary>
public class MetricsSubscriberIsolationTests
{
    [Fact]
    public async Task ExecuteNonQueryAsync_MetricsSubscriberThrows_CommandStillSucceeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var ctx = new DatabaseContext(config, factory);

        ctx.MetricsUpdated += (_, _) => throw new InvalidOperationException("subscriber failure");

        var sc = ctx.CreateSqlContainer("INSERT INTO data (v) VALUES (1)");
        var ex = await Record.ExceptionAsync(async () => await sc.ExecuteNonQueryAsync());

        Assert.Null(ex);

        var metrics = ctx.Metrics;
        Assert.True(metrics.CommandsExecuted >= 1);
        Assert.Equal(0, metrics.CommandsFailed);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_MetricsSubscriberThrows_OtherSubscriberStillInvoked()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var ctx = new DatabaseContext(config, factory);

        var wellBehavedInvocations = 0;
        ctx.MetricsUpdated += (_, _) => throw new InvalidOperationException("boom");
        ctx.MetricsUpdated += (_, _) => Interlocked.Increment(ref wellBehavedInvocations);

        var sc = ctx.CreateSqlContainer("INSERT INTO data (v) VALUES (1)");
        await sc.ExecuteNonQueryAsync();

        Assert.True(wellBehavedInvocations >= 1);
    }
}
