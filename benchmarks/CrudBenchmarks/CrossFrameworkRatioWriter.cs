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
    private const string EntityFrameworkLinqSuffix = "_EntityFramework_Linq";
    private const string EntityFrameworkCompiledSuffix = "_EntityFramework_Compiled";

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
        var grouped = new Dictionary<(string ParameterKey, string Scenario, string JobId), ScenarioMeans>();
        var correctness = BenchmarkCorrectnessArtifacts.LoadForSummary(summary.Title);

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
            var jobId = report.BenchmarkCase.Job?.Id ?? "<default>";
            var key = (parameterKey, scenario, jobId);
            if (!grouped.TryGetValue(key, out var means))
            {
                means = new ScenarioMeans();
            }

            var frameworkName = GetFrameworkName(framework);
            var isInvalid = correctness.IsInvalid(parameterKey, scenario, frameworkName);
            means.Set(framework, stats.Mean, isInvalid);
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

            if (!means.IsValid(Framework.Pengdows) || !means.IsValid(Framework.Dapper))
            {
                continue;
            }

            var pengdows = means.PengdowsMeanNs.Value;
            var dapper = means.DapperMeanNs.Value;
            var entityFramework = means.IsValid(Framework.EntityFramework)
                ? means.EntityFrameworkMeanNs
                : null;
            var linq = means.IsValid(Framework.EntityFrameworkLinq)
                ? means.EntityFrameworkLinqMeanNs
                : null;
            var compiled = means.IsValid(Framework.EntityFrameworkCompiled)
                ? means.EntityFrameworkCompiledMeanNs
                : null;

            rows.Add(new CrossRatioRow(
                item.Key.ParameterKey,
                item.Key.Scenario,
                pengdows,
                dapper,
                entityFramework,
                linq,
                compiled,
                pengdows / dapper,
                entityFramework.HasValue ? entityFramework.Value / pengdows : null,
                linq.HasValue ? linq.Value / pengdows : null,
                compiled.HasValue ? compiled.Value / pengdows : null));
        }

        return rows
            .OrderBy(row => row.ParameterKey, StringComparer.Ordinal)
            .ThenBy(row => row.Scenario, StringComparer.Ordinal)
            .ToList();
    }

    internal static string BuildMarkdown(string summaryTitle, IReadOnlyList<CrossRatioRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Cross-Framework Ratios — {summaryTitle}");
        sb.AppendLine();
        sb.AppendLine("This report computes cross-framework ratios directly from `Mean` values.");
        sb.AppendLine();
        sb.AppendLine("- `P÷D` = `Mean(_Pengdows) / Mean(_Dapper)`");
        sb.AppendLine("- `EF raw SQL ÷P` = `Mean(_EntityFramework) / Mean(_Pengdows)`: EF sending the same statement as Dapper (equal footing).");
        sb.AppendLine("- `EF LINQ ÷P` = `Mean(_EntityFramework_Linq) / Mean(_Pengdows)`: how EF is normally written.");
        sb.AppendLine("- `EF compiled ÷P` = `Mean(_EntityFramework_Compiled) / Mean(_Pengdows)`: EF's most optimized path (`EF.CompileAsyncQuery`).");
        sb.AppendLine("- BenchmarkDotNet's built-in Ratio column is baseline-relative within each group.");
        sb.AppendLine("- Rows requiring invalid framework results are excluded; a `-` means that tier was not run for the cell.");
        sb.AppendLine();

        foreach (var group in rows.GroupBy(row => row.ParameterKey))
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Pengdows Mean | Dapper Mean | EF raw SQL Mean | EF LINQ Mean | EF compiled Mean | P÷D | EF raw SQL ÷P | EF LINQ ÷P | EF compiled ÷P |");
            sb.AppendLine("|----------|--------------:|------------:|----------------:|-------------:|-----------------:|----:|--------------:|-----------:|---------------:|");
            foreach (var row in group)
            {
                sb.AppendLine(
                    $"| {row.Scenario} | {FormatMicroseconds(row.PengdowsMeanNs)} | {FormatMicroseconds(row.DapperMeanNs)} | {FormatMicroseconds(row.EntityFrameworkMeanNs)} | {FormatMicroseconds(row.EntityFrameworkLinqMeanNs)} | {FormatMicroseconds(row.EntityFrameworkCompiledMeanNs)} | {FormatRatio(row.PengdowsOverDapper)} | {FormatRatio(row.EntityFrameworkOverPengdows)} | {FormatRatio(row.EntityFrameworkLinqOverPengdows)} | {FormatRatio(row.EntityFrameworkCompiledOverPengdows)} |");
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

    internal static bool TrySplitMethod(
        string methodName,
        out string scenario,
        out Framework framework)
    {
        if (methodName.EndsWith(EntityFrameworkLinqSuffix, StringComparison.Ordinal))
        {
            scenario = methodName[..^EntityFrameworkLinqSuffix.Length];
            framework = Framework.EntityFrameworkLinq;
            return true;
        }

        if (methodName.EndsWith(EntityFrameworkCompiledSuffix, StringComparison.Ordinal))
        {
            scenario = methodName[..^EntityFrameworkCompiledSuffix.Length];
            framework = Framework.EntityFrameworkCompiled;
            return true;
        }

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

    private static string GetFrameworkName(Framework framework)
    {
        return framework switch
        {
            Framework.Pengdows => "Pengdows",
            Framework.Dapper => "Dapper",
            Framework.EntityFramework => "EntityFramework",
            Framework.EntityFrameworkLinq => "EntityFrameworkLinq",
            Framework.EntityFrameworkCompiled => "EntityFrameworkCompiled",
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null)
        };
    }

    internal enum Framework
    {
        Pengdows,
        Dapper,
        // Raw SQL through EF: the same statement Dapper sends, so this is the equal-footing EF cell.
        EntityFramework,
        // Idiomatic EF: LINQ translated by EF on each call.
        EntityFrameworkLinq,
        // Idiomatic EF with a compiled query (EF.CompileAsyncQuery): EF's most optimized path.
        EntityFrameworkCompiled
    }

    private sealed class ScenarioMeans
    {
        public double? PengdowsMeanNs { get; private set; }
        public double? DapperMeanNs { get; private set; }
        public double? EntityFrameworkMeanNs { get; private set; }
        public double? EntityFrameworkLinqMeanNs { get; private set; }
        public double? EntityFrameworkCompiledMeanNs { get; private set; }
        public bool PengdowsInvalid { get; private set; }
        public bool DapperInvalid { get; private set; }
        public bool EntityFrameworkInvalid { get; private set; }
        public bool EntityFrameworkLinqInvalid { get; private set; }
        public bool EntityFrameworkCompiledInvalid { get; private set; }

        public bool IsValid(Framework framework)
        {
            return framework switch
            {
                Framework.Pengdows => !PengdowsInvalid,
                Framework.Dapper => !DapperInvalid,
                Framework.EntityFramework => !EntityFrameworkInvalid,
                Framework.EntityFrameworkLinq => !EntityFrameworkLinqInvalid,
                Framework.EntityFrameworkCompiled => !EntityFrameworkCompiledInvalid,
                _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null)
            };
        }

        public void Set(Framework framework, double meanNanoseconds, bool isInvalid)
        {
            switch (framework)
            {
                case Framework.Pengdows:
                    PengdowsMeanNs = meanNanoseconds;
                    PengdowsInvalid = PengdowsInvalid || isInvalid;
                    break;
                case Framework.Dapper:
                    DapperMeanNs = meanNanoseconds;
                    DapperInvalid = DapperInvalid || isInvalid;
                    break;
                case Framework.EntityFramework:
                    EntityFrameworkMeanNs = meanNanoseconds;
                    EntityFrameworkInvalid = EntityFrameworkInvalid || isInvalid;
                    break;
                case Framework.EntityFrameworkLinq:
                    EntityFrameworkLinqMeanNs = meanNanoseconds;
                    EntityFrameworkLinqInvalid = EntityFrameworkLinqInvalid || isInvalid;
                    break;
                case Framework.EntityFrameworkCompiled:
                    EntityFrameworkCompiledMeanNs = meanNanoseconds;
                    EntityFrameworkCompiledInvalid = EntityFrameworkCompiledInvalid || isInvalid;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(framework), framework, null);
            }
        }
    }

    internal sealed record CrossRatioRow(
        string ParameterKey,
        string Scenario,
        double PengdowsMeanNs,
        double DapperMeanNs,
        double? EntityFrameworkMeanNs,
        double? EntityFrameworkLinqMeanNs,
        double? EntityFrameworkCompiledMeanNs,
        double PengdowsOverDapper,
        double? EntityFrameworkOverPengdows,
        double? EntityFrameworkLinqOverPengdows,
        double? EntityFrameworkCompiledOverPengdows);
}
