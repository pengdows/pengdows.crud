using System.Globalization;
using System.Text;
using BenchmarkDotNet.Reports;

namespace CrudBenchmarks;

/// <summary>
/// Writes a sidecar markdown file with explicit cross-framework ratios derived from Mean.
/// This avoids ambiguity with BenchmarkDotNet's built-in Ratio column.
/// </summary>
internal static class CrossFrameworkRatioWriter
{
    private const string PengdowsSuffix = "_Pengdows";
    private const string DapperSuffix = "_Dapper";
    private const string EntityFrameworkSuffix = "_EntityFramework";

    private static readonly string ArtifactsDir =
        Path.Combine("BenchmarkDotNet.Artifacts", "results");

    public static void Write(IEnumerable<Summary> summaries)
    {
        foreach (var summary in summaries)
        {
            WriteSummary(summary);
        }
    }

    private static void WriteSummary(Summary summary)
    {
        var rows = BuildRows(summary);
        if (rows.Count == 0)
        {
            return;
        }

        var markdown = BuildMarkdown(summary.Title, rows);

        try
        {
            Directory.CreateDirectory(ArtifactsDir);
            var outputName = $"{SanitizeFileName(summary.Title)}-cross-framework-ratios.md";
            var outputPath = Path.Combine(ArtifactsDir, outputName);
            File.WriteAllText(outputPath, markdown);
            Console.WriteLine($"[CrossFrameworkRatioWriter] Wrote {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CrossFrameworkRatioWriter] Failed to write report: {ex.Message}");
        }
    }

    private static List<CrossRatioRow> BuildRows(Summary summary)
    {
        var grouped = new Dictionary<(string ParameterKey, string Scenario), ScenarioMeans>();

        foreach (var report in summary.Reports)
        {
            var stats = report.ResultStatistics;
            if (stats == null)
            {
                continue;
            }

            var methodName = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
            if (!TrySplitMethod(methodName, out var scenario, out var framework))
            {
                continue;
            }

            var parameterKey = ExtractParameterKey(report.BenchmarkCase.DisplayInfo);
            var key = (parameterKey, scenario);
            if (!grouped.TryGetValue(key, out var means))
            {
                means = new ScenarioMeans();
            }

            means.Set(framework, stats.Mean);
            grouped[key] = means;
        }

        var rows = new List<CrossRatioRow>();
        foreach (var item in grouped)
        {
            var means = item.Value;
            if (!means.PengdowsMeanNs.HasValue || !means.DapperMeanNs.HasValue)
            {
                continue;
            }

            var pengdows = means.PengdowsMeanNs.Value;
            var dapper = means.DapperMeanNs.Value;
            var entityFramework = means.EntityFrameworkMeanNs;

            rows.Add(new CrossRatioRow(
                item.Key.ParameterKey,
                item.Key.Scenario,
                pengdows,
                dapper,
                entityFramework,
                pengdows / dapper,
                entityFramework.HasValue ? entityFramework.Value / pengdows : null));
        }

        return rows
            .OrderBy(row => row.ParameterKey, StringComparer.Ordinal)
            .ThenBy(row => row.Scenario, StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildMarkdown(string summaryTitle, IReadOnlyList<CrossRatioRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Cross-Framework Ratios — {summaryTitle}");
        sb.AppendLine();
        sb.AppendLine("This report computes cross-framework ratios directly from `Mean` values.");
        sb.AppendLine();
        sb.AppendLine("- `P÷D` = `Mean(_Pengdows) / Mean(_Dapper)`");
        sb.AppendLine("- `EF÷P` = `Mean(_EntityFramework) / Mean(_Pengdows)`");
        sb.AppendLine("- BenchmarkDotNet's built-in Ratio column is baseline-relative within each group.");
        sb.AppendLine();

        foreach (var group in rows.GroupBy(row => row.ParameterKey))
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Pengdows Mean | Dapper Mean | EF Mean | P÷D | EF÷P |");
            sb.AppendLine("|----------|--------------:|------------:|--------:|----:|-----:|");
            foreach (var row in group)
            {
                sb.AppendLine(
                    $"| {row.Scenario} | {FormatMicroseconds(row.PengdowsMeanNs)} | {FormatMicroseconds(row.DapperMeanNs)} | {FormatMicroseconds(row.EntityFrameworkMeanNs)} | {FormatRatio(row.PengdowsOverDapper)} | {FormatRatio(row.EntityFrameworkOverPengdows)} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatMicroseconds(double? nanoseconds)
    {
        if (!nanoseconds.HasValue)
        {
            return "-";
        }

        var microseconds = nanoseconds.Value / 1_000d;
        return $"{microseconds.ToString("N2", CultureInfo.InvariantCulture)} us";
    }

    private static string FormatRatio(double? ratio)
    {
        return ratio.HasValue
            ? ratio.Value.ToString("0.000", CultureInfo.InvariantCulture)
            : "-";
    }

    private static string ExtractParameterKey(string displayInfo)
    {
        var start = displayInfo.IndexOf('[');
        var end = displayInfo.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return displayInfo.Substring(start + 1, end - start - 1).Trim();
        }

        return "No parameters";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name
            .Select(c => invalid.Contains(c) ? '_' : c)
            .ToArray();

        return new string(chars);
    }

    private static bool TrySplitMethod(
        string methodName,
        out string scenario,
        out Framework framework)
    {
        if (methodName.EndsWith(PengdowsSuffix, StringComparison.Ordinal))
        {
            scenario = methodName[..^PengdowsSuffix.Length];
            framework = Framework.Pengdows;
            return true;
        }

        if (methodName.EndsWith(DapperSuffix, StringComparison.Ordinal))
        {
            scenario = methodName[..^DapperSuffix.Length];
            framework = Framework.Dapper;
            return true;
        }

        if (methodName.EndsWith(EntityFrameworkSuffix, StringComparison.Ordinal))
        {
            scenario = methodName[..^EntityFrameworkSuffix.Length];
            framework = Framework.EntityFramework;
            return true;
        }

        scenario = string.Empty;
        framework = default;
        return false;
    }

    private enum Framework
    {
        Pengdows,
        Dapper,
        EntityFramework
    }

    private sealed class ScenarioMeans
    {
        public double? PengdowsMeanNs { get; private set; }
        public double? DapperMeanNs { get; private set; }
        public double? EntityFrameworkMeanNs { get; private set; }

        public void Set(Framework framework, double meanNanoseconds)
        {
            switch (framework)
            {
                case Framework.Pengdows:
                    PengdowsMeanNs = meanNanoseconds;
                    break;
                case Framework.Dapper:
                    DapperMeanNs = meanNanoseconds;
                    break;
                case Framework.EntityFramework:
                    EntityFrameworkMeanNs = meanNanoseconds;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(framework), framework, null);
            }
        }
    }

    private sealed record CrossRatioRow(
        string ParameterKey,
        string Scenario,
        double PengdowsMeanNs,
        double DapperMeanNs,
        double? EntityFrameworkMeanNs,
        double PengdowsOverDapper,
        double? EntityFrameworkOverPengdows);
}
