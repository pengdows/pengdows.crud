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
/// Also covers a second, independent bypass: Activity.SetStatus(ActivityStatusCode.Error,
/// ex.Message) carried the same unbounded text through a path the exception.message truncation
/// never reached.
/// </summary>
/// <remarks>
/// This branch's SqlContainer does not tag activities with a per-context correlation id (unlike
/// the 3.0 branch's "pengdows.context_id"), and this test project runs collections in parallel
/// (xunit.runner.json: parallelizeTestCollections=true) — a bare `source.Name == "pengdows.crud"`
/// filter could pick up another concurrently-running test's activity. Each test here instead
/// starts its own parent Activity on a private ActivitySource and correlates strictly by
/// ParentId, which is reliable under parallel execution because System.Diagnostics.Activity sets
/// a new activity's Parent from the ambient Activity.Current at creation time.
/// </remarks>
public class SqlContainerTelemetryRedactionTests
{
    private const string TestCorrelationSourceName = "pengdows.crud.Tests.TelemetryRedaction";
    private static readonly ActivitySource TestCorrelationSource = new(TestCorrelationSourceName);

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
            ShouldListenTo = source => source.Name == "pengdows.crud" || source.Name == TestCorrelationSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { }
        };
        ActivitySource.AddActivityListener(listener);

        using var root = TestCorrelationSource.StartActivity("root");
        listener.ActivityStopped = activity =>
        {
            if (root != null && activity.ParentId == root.Id)
            {
                dbStatement = activity.GetTagItem("db.statement") as string;
            }
        };

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
            ShouldListenTo = source => source.Name == "pengdows.crud" || source.Name == TestCorrelationSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { }
        };
        ActivitySource.AddActivityListener(listener);

        using var root = TestCorrelationSource.StartActivity("root");
        listener.ActivityStopped = activity =>
        {
            if (root == null || activity.ParentId != root.Id)
            {
                return;
            }

            var found = activity.Events.FirstOrDefault(e => e.Name == "exception");
            if (found.Name == "exception")
            {
                exceptionEvent = found;
            }
        };

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

    [Fact]
    public async Task ExecuteNonQuery_OnFailure_ActivityStatusDescription_DoesNotCarryUnboundedMessage()
    {
        // The exception.message tag (asserted above) is deliberately bounded — but
        // Activity.SetStatus(ActivityStatusCode.Error, ex.Message) is a second, independent path
        // that can carry the exact same unbounded provider message, bypassing that policy.
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

        failingConnection.SetCommandFailure("SELECT 1", new InvalidOperationException(longMessage));

        string? statusDescription = null;
        var statusDescriptionCaptured = false;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "pengdows.crud" || source.Name == TestCorrelationSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { }
        };
        ActivitySource.AddActivityListener(listener);

        using var root = TestCorrelationSource.StartActivity("root");
        listener.ActivityStopped = activity =>
        {
            if (root == null || activity.ParentId != root.Id)
            {
                return;
            }

            if (activity.Status == ActivityStatusCode.Error)
            {
                statusDescriptionCaptured = true;
                statusDescription = activity.StatusDescription;
            }
        };

        var container = ctx.CreateSqlContainer("SELECT 1");
        await Assert.ThrowsAnyAsync<Exception>(async () => await container.ExecuteNonQueryAsync());

        Assert.True(statusDescriptionCaptured);
        Assert.True(string.IsNullOrEmpty(statusDescription) || statusDescription!.Length < 5_000,
            "Activity.StatusDescription must not carry the full, unbounded exception message — " +
            "the bounded exception.message event tag already carries that diagnostic information.");
    }
}
