using System.Text.Json;

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

    private static readonly string ArtifactsDir =
        Path.Combine("BenchmarkDotNet.Artifacts", "results");

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

    public static int CountFailures(string summaryTitle, string parameterKey, string scenario, string frameworkName)
    {
        var path = GetPath(ExtractBenchmarkClassName(summaryTitle));
        if (!File.Exists(path))
            return 0;

        try
        {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<CorrectnessArtifact>(json, SerializerOptions);
            if (payload?.Issues == null)
                return 0;

            var normalizedParam = string.IsNullOrWhiteSpace(parameterKey) ? "*" : parameterKey.Trim();
            return payload.Issues
                .Where(issue =>
                    string.Equals(string.IsNullOrWhiteSpace(issue.ParameterKey) ? "*" : issue.ParameterKey, normalizedParam, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(issue.Scenario, scenario, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(issue.Framework, frameworkName, StringComparison.OrdinalIgnoreCase))
                .Sum(issue => issue.Count);
        }
        catch
        {
            return 0;
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
