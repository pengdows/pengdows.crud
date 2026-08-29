using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CORE-024: SqlContainer.StartActivity() previously recorded the full, untruncated SQL text as
/// db.statement and, on failure, the complete exception message plus a raw exception.stacktrace
/// tag — with no bound and no redaction. A pathological custom-SQL block could balloon trace
/// storage; provider exception text (and definitely a stack trace) can carry server names,
/// connection internals, or SQL fragments that don't belong in a telemetry backend's tags.
/// </summary>
public class SqlContainerTelemetryRedactionTests
{
    private static DatabaseContext CreateContext(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        };

        return new DatabaseContext(cfg, factory, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task ExecuteNonQuery_Activity_TruncatesOversizedDbStatement()
    {
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        string? dbStatement = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "pengdows.crud",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("pengdows.context_id") as string == ctx.RootId.ToString())
                {
                    dbStatement = activity.GetTagItem("db.statement") as string;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var container = ctx.CreateSqlContainer("SELECT 1");
        // Append a pathologically long comment so the raw query text is far larger than any
        // reasonable truncation bound.
        container.Query.Append(' ').Append(new string('x', 20_000));

        await container.ExecuteNonQueryAsync();

        Assert.NotNull(dbStatement);
        Assert.True(dbStatement!.Length < 20_000,
            "db.statement must be truncated rather than recording the full, unbounded query text.");
    }

    [Fact]
    public async Task ExecuteNonQuery_OnFailure_Activity_OmitsStackTraceAndTruncatesMessage()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var longMessage = new string('e', 5_000);
        var failingConnection = new fakeDbConnection();
        failingConnection.SetCommandFailure("SELECT 1", new InvalidOperationException(longMessage));
        factory.Connections.Add(failingConnection);

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        using var ctx = new DatabaseContext(cfg, factory, NullLoggerFactory.Instance);

        // Re-assert the failure after initialization probes to ensure the test command hits it.
        failingConnection.SetCommandFailure("SELECT 1", new InvalidOperationException(longMessage));

        ActivityEvent? exceptionEvent = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "pengdows.crud",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("pengdows.context_id") as string == ctx.RootId.ToString())
                {
                    var found = activity.Events.FirstOrDefault(e => e.Name == "exception");
                    if (found.Name == "exception")
                    {
                        exceptionEvent = found;
                    }
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var container = ctx.CreateSqlContainer("SELECT 1");
        await Assert.ThrowsAnyAsync<Exception>(async () => await container.ExecuteNonQueryAsync());

        Assert.NotNull(exceptionEvent);
        var tags = exceptionEvent!.Value.Tags.ToDictionary(t => t.Key, t => t.Value);

        Assert.False(tags.ContainsKey("exception.stacktrace"),
            "exception.stacktrace must not be recorded as an activity tag — stack traces belong in logs.");

        Assert.True(tags.TryGetValue("exception.message", out var message));
        var messageText = message as string;
        Assert.NotNull(messageText);
        Assert.True(messageText!.Length < 5_000,
            "exception.message must be truncated rather than recording the full, unbounded text.");
    }
}
