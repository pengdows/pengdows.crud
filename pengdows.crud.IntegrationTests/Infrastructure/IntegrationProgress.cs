using System.Collections.Concurrent;
using System.Text;
using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// An always-on record of an integration run (DEC-011): <c>progress.log</c> gets a line as each test
/// starts and ends on each database and as each container starts, a heartbeat once a minute names
/// what is still running, and <c>summary-&lt;tfm&gt;.md</c> (written when the fixture is disposed, and at process
/// exit) lists pass/fail/skip per database, the slowest tests and the first line of each failure.
/// Both files go to <c>TestResults/integration</c> under the repository root, or
/// <c>INTEGRATION_RESULTS_DIR</c>.
/// </summary>
internal sealed class IntegrationProgress
{
    private static readonly Lazy<IntegrationProgress> SharedInstance = new(() =>
    {
        var progress = new IntegrationProgress(ResolveDirectory(), () => DateTime.UtcNow, TimeSpan.FromMinutes(1),
            RunLabel(TargetFramework(), Environment.GetEnvironmentVariable("INTEGRATION_ONLY")));
        progress.Append($"=== run started {DateTime.UtcNow:u} ({TargetFramework()}, pid {Environment.ProcessId})");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => progress.TryWriteSummary();
        return progress;
    });

    private readonly Func<DateTime> _clock;
    private readonly object _fileGate = new();
    private readonly ConcurrentDictionary<(string Test, SupportedDatabase Provider), DateTime> _running = new();
    private readonly ConcurrentDictionary<SupportedDatabase, DateTime> _containersStarting = new();
    private readonly ConcurrentQueue<Outcome> _outcomes = new();
    private readonly Timer? _heartbeat;

    private enum Result
    {
        Pass,
        Fail,
        Skip
    }

    private sealed record Outcome(string Test, SupportedDatabase Provider, Result Result, TimeSpan Elapsed, string? Detail);

    public static IntegrationProgress Shared => SharedInstance.Value;

    internal IntegrationProgress(string directory, Func<DateTime> clock, TimeSpan? heartbeatInterval,
        string? runLabel = null)
    {
        _clock = clock;
        Directory.CreateDirectory(directory);
        ProgressPath = Path.Combine(directory, "progress.log");
        // One summary per run label (RunLabel): framework, and database when the run is limited.
        SummaryPath = Path.Combine(directory, runLabel is null ? "summary.md" : $"summary-{runLabel}.md");
        if (heartbeatInterval is { } interval)
        {
            _heartbeat = new Timer(_ => WriteHeartbeat(), null, interval, interval);
        }
    }

    public string ProgressPath { get; }

    public string SummaryPath { get; }

    public void ContainerStarting(SupportedDatabase provider)
    {
        _containersStarting[provider] = _clock();
        Append($"{provider} container starting");
    }

    public void ContainerReady(SupportedDatabase provider)
    {
        Append($"{provider} container ready ({Since(_containersStarting.TryRemove(provider, out var start) ? start : _clock())})");
    }

    public void ContainerFailed(SupportedDatabase provider, string reason)
    {
        _containersStarting.TryRemove(provider, out _);
        Append($"{provider} container failed: {FirstLine(reason)}");
    }

    public void TestStarted(string test, SupportedDatabase provider)
    {
        _running[(test, provider)] = _clock();
        Append($"start {provider} {test}");
    }

    public void TestPassed(string test, SupportedDatabase provider) => Finish(test, provider, Result.Pass, null);

    public void TestFailed(string test, SupportedDatabase provider, Exception error) =>
        Finish(test, provider, Result.Fail, $"{error.GetType().Name}: {FirstLine(error.Message)}");

    public void TestSkipped(string test, SupportedDatabase provider, string reason) =>
        Finish(test, provider, Result.Skip, FirstLine(reason));

    /// <summary>The heartbeat line for the tests still running, or null when nothing is.</summary>
    public string? Heartbeat()
    {
        var now = _clock();
        var running = _running
            .OrderBy(r => r.Value)
            .Select(r => $"{r.Key.Test} on {r.Key.Provider} ({(now - r.Value).TotalSeconds:F0}s)")
            .ToList();
        return running.Count == 0 ? null : "heartbeat: running " + string.Join(", ", running);
    }

    public string WriteSummary()
    {
        var outcomes = _outcomes.ToArray();
        var text = new StringBuilder();
        text.AppendLine("# Integration run summary").AppendLine();
        text.AppendLine($"Written {_clock():u}; {outcomes.Length} test results.").AppendLine();
        text.AppendLine("| Database | Pass | Fail | Skip |").AppendLine("|---|---:|---:|---:|");
        foreach (var group in outcomes.GroupBy(o => o.Provider).OrderBy(g => g.Key.ToString()))
        {
            text.AppendLine(
                $"| {group.Key} | {group.Count(o => o.Result == Result.Pass)} | {group.Count(o => o.Result == Result.Fail)} | {group.Count(o => o.Result == Result.Skip)} |");
        }

        var failures = outcomes.Where(o => o.Result == Result.Fail).ToList();
        text.AppendLine().AppendLine($"## Failures ({failures.Count})").AppendLine();
        foreach (var failure in failures)
        {
            text.AppendLine($"- {failure.Test} ({failure.Provider}): {failure.Detail}");
        }

        text.AppendLine().AppendLine("## Slowest tests").AppendLine();
        text.AppendLine("| Test | Database | Time |").AppendLine("|---|---|---:|");
        foreach (var slow in outcomes.Where(o => o.Result != Result.Skip).OrderByDescending(o => o.Elapsed).Take(15))
        {
            text.AppendLine($"| {slow.Test} | {slow.Provider} | {slow.Elapsed.TotalSeconds:F1}s |");
        }

        var summary = text.ToString();
        lock (_fileGate)
        {
            File.WriteAllText(SummaryPath, summary);
        }

        return summary;
    }

    internal void TryWriteSummary()
    {
        try
        {
            _heartbeat?.Dispose();
            if (!_outcomes.IsEmpty)
            {
                WriteSummary();
            }
        }
        catch
        {
            // Best effort at shutdown; the progress log already has every line.
        }
    }

    private void Finish(string test, SupportedDatabase provider, Result result, string? detail)
    {
        var elapsed = _running.TryRemove((test, provider), out var start) ? _clock() - start : TimeSpan.Zero;
        _outcomes.Enqueue(new Outcome(test, provider, result, elapsed, detail));
        var label = result switch { Result.Pass => "pass", Result.Fail => "FAIL", _ => "skip" };
        Append($"{label} {provider} {test} ({elapsed.TotalSeconds:F1}s){(detail is null ? "" : ": " + detail)}");
    }

    private void WriteHeartbeat()
    {
        var line = Heartbeat();
        if (line != null)
        {
            Append(line);
        }
    }

    private string Since(DateTime start) => $"{(_clock() - start).TotalSeconds:F1}s";

    // "net10.0" from the running runtime's major.minor.
    private static string TargetFramework() => $"net{Environment.Version.Major}.{Environment.Version.Minor}";

    private void Append(string message)
    {
        var line = $"[{_clock():HH:mm:ss}] {message}";
        try
        {
            lock (_fileGate)
            {
                File.AppendAllText(ProgressPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // A progress line is never worth failing a test.
        }
    }

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return (end < 0 ? text : text[..end]).Trim();
    }

    /// <summary>
    /// The summary's name: the target framework, and the databases when the run is limited to some
    /// (run-integration-tests.sh runs one process per database, two at a time).
    /// </summary>
    internal static string RunLabel(string framework, string? only)
    {
        var databases = (only ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return databases.Length == 0 ? framework : framework + "-" + string.Join("-", databases);
    }

    private static string ResolveDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("INTEGRATION_RESULTS_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "pengdows.crud.sln")))
            {
                return Path.Combine(dir.FullName, "TestResults", "integration");
            }
        }

        return Path.Combine(Path.GetTempPath(), "pengdows-integration");
    }
}
