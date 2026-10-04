using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// DEC-011: an always-on record of an integration run, so a long run shows what it is doing while it
/// runs and what failed when it ends, without INTEGRATION_TRACE.
/// </summary>
public sealed class IntegrationProgressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pengdows-progress-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private IntegrationProgress Create() => new(_dir, () => _now, heartbeatInterval: null);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void TestLifecycle_IsWrittenToTheProgressLogAsItHappens()
    {
        var progress = Create();

        progress.TestStarted("Insert_Works", SupportedDatabase.PostgreSql);
        _now = _now.AddSeconds(2);
        progress.TestPassed("Insert_Works", SupportedDatabase.PostgreSql);

        var lines = File.ReadAllLines(progress.ProgressPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("start", lines[0]);
        Assert.Contains("PostgreSql", lines[0]);
        Assert.Contains("Insert_Works", lines[0]);
        Assert.Contains("pass", lines[1]);
        Assert.Contains("2.0s", lines[1]);
    }

    [Fact]
    public void Heartbeat_NamesEachRunningTestDatabaseAndElapsedTime()
    {
        var progress = Create();
        Assert.Null(progress.Heartbeat());

        progress.TestStarted("Slow_One", SupportedDatabase.Oracle);
        _now = _now.AddMinutes(3);

        var heartbeat = progress.Heartbeat();
        Assert.NotNull(heartbeat);
        Assert.Contains("Slow_One", heartbeat);
        Assert.Contains("Oracle", heartbeat);
        Assert.Contains("180s", heartbeat);

        progress.TestPassed("Slow_One", SupportedDatabase.Oracle);
        Assert.Null(progress.Heartbeat());
    }

    [Fact]
    public void ContainerLifecycle_IsRecorded()
    {
        var progress = Create();

        progress.ContainerStarting(SupportedDatabase.Db2);
        _now = _now.AddSeconds(40);
        progress.ContainerReady(SupportedDatabase.Db2);
        progress.ContainerFailed(SupportedDatabase.Informix, "native client missing\nsecond line");

        var log = File.ReadAllText(progress.ProgressPath);
        Assert.Contains("Db2 container starting", log);
        Assert.Contains("Db2 container ready (40.0s)", log);
        Assert.Contains("Informix container failed: native client missing", log);
        Assert.DoesNotContain("second line", log);
    }

    [Fact]
    public void Summary_CountsOutcomesPerDatabase_ListsSlowestAndFirstFailureLines()
    {
        var progress = Create();
        progress.TestStarted("A", SupportedDatabase.MySql);
        _now = _now.AddSeconds(1);
        progress.TestPassed("A", SupportedDatabase.MySql);
        progress.TestStarted("B", SupportedDatabase.MySql);
        _now = _now.AddSeconds(9);
        progress.TestFailed("B", SupportedDatabase.MySql, new InvalidOperationException("boom\nstack junk"));
        progress.TestSkipped("C", SupportedDatabase.Sqlite, "capability: no stored procedures");

        var summary = progress.WriteSummary();

        Assert.True(File.Exists(progress.SummaryPath));
        Assert.Contains("| MySql | 1 | 1 | 0 |", summary);
        Assert.Contains("| Sqlite | 0 | 0 | 1 |", summary);
        Assert.Contains("B (MySql): InvalidOperationException: boom", summary);
        Assert.DoesNotContain("stack junk", summary);
        Assert.True(summary.IndexOf("| B | MySql | 9.0s |", StringComparison.Ordinal) <
                    summary.IndexOf("| A | MySql | 1.0s |", StringComparison.Ordinal));
    }
}
