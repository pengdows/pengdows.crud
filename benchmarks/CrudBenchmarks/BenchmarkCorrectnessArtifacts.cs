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

    public static void Write(string benchmarkClassName, IReadOnlyCollection<CorrectnessIssue> issues)
    {
        try
        {
            Directory.CreateDirectory(ArtifactsDir);
            var path = GetPath(benchmarkClassName);
            var payload = new CorrectnessArtifact(benchmarkClassName, DateTime.UtcNow, issues.ToArray());
            var json = JsonSerializer.Serialize(payload, SerializerOptions);
            File.WriteAllText(path, json);
            Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Wrote {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Failed to write correctness artifact: {ex.Message}");
        }
    }

    public static CorrectnessIssueLookup LoadForSummary(string summaryTitle)
    {
        var benchmarkClassName = ExtractBenchmarkClassName(summaryTitle);
        var path = GetPath(benchmarkClassName);
        if (!File.Exists(path))
        {
            return CorrectnessIssueLookup.Empty;
        }

        try
        {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<CorrectnessArtifact>(json, SerializerOptions);
            if (payload?.Issues == null || payload.Issues.Length == 0)
            {
                return CorrectnessIssueLookup.Empty;
            }

            return new CorrectnessIssueLookup(payload.Issues);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Failed to read correctness artifact: {ex.Message}");
            return CorrectnessIssueLookup.Empty;
        }
    }

    /// <summary>
    /// Returns the recorded failure count for this benchmark/scenario/framework, or
    /// <c>null</c> if the correctness artifact itself could not be found or read. Callers MUST
    /// treat <c>null</c> differently from <c>0</c> — <c>0</c> means the artifact was found and
    /// genuinely recorded no matching issues; <c>null</c> means correctness was never verified
    /// for this run and nothing should be inferred from it either way.
    /// </summary>
    public static int? CountFailures(string summaryTitle, string parameterKey, string scenario, string frameworkName)
    {
        var path = GetPath(ExtractBenchmarkClassName(summaryTitle));
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<CorrectnessArtifact>(json, SerializerOptions);
            if (payload?.Issues == null)
                return null;

            var normalizedParam = string.IsNullOrWhiteSpace(parameterKey) ? "*" : parameterKey.Trim();
            return payload.Issues
                .Where(issue =>
                    string.Equals(string.IsNullOrWhiteSpace(issue.ParameterKey) ? "*" : issue.ParameterKey, normalizedParam, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(issue.Scenario, scenario, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(issue.Framework, frameworkName, StringComparison.OrdinalIgnoreCase))
                .Sum(issue => issue.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BenchmarkCorrectnessArtifacts] Failed to read correctness artifact: {ex.Message}");
            return null;
        }
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

    private static string GetPath(string benchmarkClassName)
    {
        return Path.Combine(ArtifactsDir, $"{benchmarkClassName}{FileSuffix}");
    }

    private sealed record CorrectnessArtifact(
        string BenchmarkClassName,
        DateTime GeneratedUtc,
        CorrectnessIssue[] Issues);
}
