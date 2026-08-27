using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("CrudBenchmarks.Tests")]

namespace CrudBenchmarks;

/// <summary>
/// Composite key for deduplicating correctness issues across concurrent benchmark iterations.
/// Shared by all benchmark classes that track per-scenario framework correctness.
/// </summary>
internal readonly record struct CorrectnessIssueKey(
    string ParameterKey,
    string Scenario,
    string Framework,
    string Reason);

internal sealed record CorrectnessIssue(
    string? ParameterKey,
    string Scenario,
    string Framework,
    string Reason,
    int Count);

internal sealed class CorrectnessIssueLookup
{
    public static readonly CorrectnessIssueLookup Empty = new(Array.Empty<CorrectnessIssue>());

    private readonly HashSet<string> _invalidExact;
    private readonly HashSet<string> _invalidWildcard;

    public CorrectnessIssueLookup(IEnumerable<CorrectnessIssue> issues)
    {
        _invalidExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _invalidWildcard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var issue in issues)
        {
            if (string.IsNullOrWhiteSpace(issue.Scenario) || string.IsNullOrWhiteSpace(issue.Framework))
            {
                continue;
            }

            var parameterKey = string.IsNullOrWhiteSpace(issue.ParameterKey) ? "*" : issue.ParameterKey.Trim();
            var key = BuildKey(parameterKey, issue.Scenario, issue.Framework);
            if (parameterKey == "*")
            {
                _invalidWildcard.Add(key);
            }
            else
            {
                _invalidExact.Add(key);
            }
        }
    }

    public bool IsInvalid(string parameterKey, string scenario, string framework)
    {
        var normalizedParameter = string.IsNullOrWhiteSpace(parameterKey) ? "No parameters" : parameterKey.Trim();
        var exact = BuildKey(normalizedParameter, scenario, framework);
        if (_invalidExact.Contains(exact))
        {
            return true;
        }

        var wildcard = BuildKey("*", scenario, framework);
        return _invalidWildcard.Contains(wildcard);
    }

    private static string BuildKey(string parameterKey, string scenario, string framework)
    {
        return $"{parameterKey}\u001f{scenario}\u001f{framework}";
    }
}

internal static class BenchmarkCorrectnessArtifacts
{
    private const string FileSuffix = "-correctness.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // BenchmarkDotNet's default out-of-process toolchain runs each benchmark's
    // [GlobalSetup]/[GlobalCleanup] inside a separately compiled child process, whose current
    // directory is a generated, per-run project directory under
    // CrudBenchmarks/bin/<config>/<tfm>/<guid>/ — one BenchmarkDotNet deletes during its own
    // artifact cleanup after the run. Writing to a path relative to that directory made this
    // artifact unrecoverable (confirmed: see
    // benchmarks/CrudBenchmarks/results/sqlite-write-contention-run-2026-08-13.md's "Note on
    // artifact durability"). Program.Main sets CRUD_BENCH_ARTIFACTS_DIR to an absolute,
    // stable path before BenchmarkSwitcher runs anything, and child processes inherit it.
    private static string ArtifactsDir =>
        Environment.GetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR")
        ?? Path.Combine("BenchmarkDotNet.Artifacts", "results");

    // Each [Benchmark] method (Pengdows/Dapper/EntityFramework) runs in its OWN separately
    // spawned process with a fresh instance, so a single class-scoped file written with
    // File.WriteAllText meant whichever process's Cleanup() ran (or completed) LAST silently
    // overwrote every other framework's recorded issues — confirmed in practice on
    // 2026-08-27: ConnectionPoolProtectionBenchmarks' correctness.json only ever contained
    // whichever framework/scenario happened to run dead last across the whole class, so
    // "Fails: 0" for every other row (including every Pengdows row) was unverified, not
    // actually confirmed clean, even when Pengdows genuinely had zero issues. Fixed by giving
    // each process its own fragment file (keyed by process ID, so concurrent/sequential
    // processes never collide) and merging all fragments for a class at read time instead of
    // relying on a single shared file surviving every process's turn to write it.
    private static string FragmentsDir => Path.Combine(ArtifactsDir, "correctness-fragments");

    public static void Write(string benchmarkClassName, IReadOnlyCollection<CorrectnessIssue> issues, long? totalAttempted = null)
    {
        try
        {
            Directory.CreateDirectory(FragmentsDir);
            var path = GetFragmentPath(benchmarkClassName, Environment.ProcessId);
            var payload = new CorrectnessArtifact(benchmarkClassName, DateTime.UtcNow, issues.ToArray(), totalAttempted);
            var json = JsonSerializer.Serialize(payload, SerializerOptions);
            File.WriteAllText(path, json);
            Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Wrote {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Failed to write correctness artifact: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes any fragment files left over from a previous run, so a fresh run's merged view
    /// can't be polluted by stale data from a class that isn't even part of this run's filter.
    /// Call once, in the parent process, before BenchmarkSwitcher runs anything.
    /// </summary>
    public static void ClearFragmentsFromPreviousRun()
    {
        try
        {
            if (Directory.Exists(FragmentsDir))
            {
                Directory.Delete(FragmentsDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Failed to clear stale fragments: {ex.Message}");
        }
    }

    public static CorrectnessIssueLookup LoadForSummary(string summaryTitle)
    {
        var issues = LoadMergedIssues(ExtractBenchmarkClassName(summaryTitle));
        return issues.Count == 0 ? CorrectnessIssueLookup.Empty : new CorrectnessIssueLookup(issues);
    }

    /// <summary>
    /// Returns the recorded failure count for this benchmark/scenario/framework, or
    /// <c>null</c> if no fragment for this class could be found/read at all. Callers MUST
    /// treat <c>null</c> differently from <c>0</c> — <c>0</c> means at least one fragment was
    /// found and genuinely recorded no matching issues; <c>null</c> means correctness was
    /// never verified for this run and nothing should be inferred from it either way.
    /// </summary>
    public static int? CountFailures(string summaryTitle, string? parameterKey, string scenario, string frameworkName)
    {
        var benchmarkClassName = ExtractBenchmarkClassName(summaryTitle);
        if (!Directory.Exists(FragmentsDir) ||
            Directory.GetFiles(FragmentsDir, $"{benchmarkClassName}-*-correctness.json").Length == 0)
        {
            return null;
        }

        var normalizedParam = string.IsNullOrWhiteSpace(parameterKey) ? "*" : parameterKey.Trim();
        return LoadMergedIssues(benchmarkClassName)
            .Where(issue =>
                string.Equals(string.IsNullOrWhiteSpace(issue.ParameterKey) ? "*" : issue.ParameterKey, normalizedParam, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(issue.Scenario, scenario, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(issue.Framework, frameworkName, StringComparison.OrdinalIgnoreCase))
            .Sum(issue => issue.Count);
    }

    /// <summary>
    /// True if this class wrote at least one correctness fragment for this run — i.e. it DOES
    /// track correctness, so a caller that can't resolve a specific method's framework/scenario
    /// identity is looking at a resolution bug, not simply an untracked class.
    /// </summary>
    public static bool HasFragmentsFor(string summaryTitle)
    {
        var benchmarkClassName = ExtractBenchmarkClassName(summaryTitle);
        return Directory.Exists(FragmentsDir) &&
            Directory.GetFiles(FragmentsDir, $"{benchmarkClassName}-*-correctness.json").Length > 0;
    }

    private static List<CorrectnessIssue> LoadMergedIssues(string benchmarkClassName)
    {
        var merged = new List<CorrectnessIssue>();
        if (!Directory.Exists(FragmentsDir))
        {
            return merged;
        }

        foreach (var path in Directory.GetFiles(FragmentsDir, $"{benchmarkClassName}-*-correctness.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var payload = JsonSerializer.Deserialize<CorrectnessArtifact>(json, SerializerOptions);
                if (payload?.Issues != null)
                {
                    merged.AddRange(payload.Issues);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Failed to read fragment {path}: {ex.Message}");
            }
        }

        return merged;
    }

    private static string ExtractBenchmarkClassName(string summaryTitle)
    {
        var titleWithoutTimestamp = summaryTitle;
        var timestampStart = summaryTitle.LastIndexOf('-');
        if (timestampStart > 0)
        {
            var previousDash = summaryTitle.LastIndexOf('-', timestampStart - 1);
            if (previousDash > 0)
            {
                titleWithoutTimestamp = summaryTitle[..previousDash];
            }
        }

        var lastDot = titleWithoutTimestamp.LastIndexOf('.');
        return lastDot >= 0 ? titleWithoutTimestamp[(lastDot + 1)..] : titleWithoutTimestamp;
    }

    private static string GetFragmentPath(string benchmarkClassName, int processId)
    {
        return Path.Combine(FragmentsDir, $"{benchmarkClassName}-{processId}{FileSuffix}");
    }

    private sealed record CorrectnessArtifact(
        string BenchmarkClassName,
        DateTime GeneratedUtc,
        CorrectnessIssue[] Issues,
        long? TotalAttempted = null);
}
