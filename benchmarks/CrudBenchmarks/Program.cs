using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using Perfolizer.Horology;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace CrudBenchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var includeOptInBenchmarks = IsOptInBenchmarkEnabled(args);
        var switcherArgs = RemoveOptInFlag(args);

        // BenchmarkDotNet's out-of-process toolchain runs each benchmark's [GlobalSetup]/
        // [GlobalCleanup] in a separately compiled child process whose current directory is a
        // generated, per-run directory that gets deleted by BDN's own artifact cleanup. Resolve
        // and export an absolute, stable results path now, in this (parent) process, before
        // BenchmarkSwitcher launches anything — child processes inherit environment variables,
        // so BenchmarkCorrectnessArtifacts and the tx-latency sidecar writer can read this
        // instead of writing to a path relative to whatever directory they happen to run from.
        var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkDotNet.Artifacts", "results");
        Directory.CreateDirectory(resultsDir);
        Environment.SetEnvironmentVariable("CRUD_BENCH_ARTIFACTS_DIR", resultsDir);

        // Clear correctness fragments from any previous run before this one starts, so a
        // merged read for a class in THIS run never picks up stale issues left behind by an
        // earlier run (e.g. a class excluded from this run's --filter).
        BenchmarkCorrectnessArtifacts.ClearFragmentsFromPreviousRun();

        IConfig config = ShouldUseInProcess()
            ? new InProcessConfig()
            : new BenchmarkConfig();

        var benchmarkTypes = GetBenchmarkTypes(includeOptInBenchmarks);
        var summaries = BenchmarkSwitcher.FromTypes(benchmarkTypes).Run(switcherArgs, config);
        CrossFrameworkRatioWriter.Write(summaries);
    }

    private static Type[] GetBenchmarkTypes(bool includeOptInBenchmarks)
    {
        var assembly = typeof(Program).Assembly;
        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }

        return types
            .Where(type => !type.IsAbstract && HasBenchmarkMethods(type))
            .Where(type => includeOptInBenchmarks || !IsOptInBenchmark(type))
            .ToArray();
    }

    private static bool IsOptInBenchmark(Type type)
    {
        return type.GetCustomAttributes(typeof(OptInBenchmarkAttribute), inherit: false).Length != 0;
    }

    private static bool HasBenchmarkMethods(Type type)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(method => method.GetCustomAttributes(typeof(BenchmarkAttribute), inherit: true).Length != 0);
    }

    private static bool IsOptInBenchmarkEnabled(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--include-opt-in", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var value = Environment.GetEnvironmentVariable("CRUD_BENCH_INCLUDE_OPT_IN");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] RemoveOptInFlag(string[] args)
    {
        return args
            .Where(arg => !string.Equals(arg, "--include-opt-in", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static bool ShouldUseInProcess()
    {
        var value = Environment.GetEnvironmentVariable("CRUD_BENCH_INPROC");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class InProcessConfig : ManualConfig
    {
        public InProcessConfig()
        {
            ArtifactsPath = Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkDotNet.Artifacts");
            AddLogger(ConsoleLogger.Default);
            AddColumnProvider(DefaultColumnProviders.Instance);
            AddColumn(StatisticColumn.P95);
            AddColumn(new PercentileColumn("P99", 99));
            AddColumn(new CorrectnessColumn());
            AddJob(Job.Default
                .WithToolchain(InProcessNoEmitToolchain.Instance)
                .WithId("InProcess"));
        }
    }

    private sealed class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            ArtifactsPath = Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkDotNet.Artifacts");
            AddLogger(ConsoleLogger.Default);
            AddColumnProvider(DefaultColumnProviders.Instance);
            AddColumn(StatisticColumn.P95);
            AddColumn(new PercentileColumn("P99", 99));
            AddColumn(new CorrectnessColumn());
            AddExporter(MarkdownExporter.GitHub);
            AddExporter(CsvExporter.Default);
            AddExporter(HtmlExporter.Default);
        }
    }

    private sealed class PercentileColumn : IColumn
    {
        private readonly int _percentile;

        public PercentileColumn(string columnName, int percentile)
        {
            _percentile = percentile;
            ColumnName = columnName;
            Id = $"Percentile.{columnName}";
        }

        public string Id { get; }
        public string ColumnName { get; }
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Statistics;
        public int PriorityInCategory => _percentile;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Time;
        public string Legend => $"Percentile {_percentile} ({_percentile}% of all measurements fell below this value)";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            return GetValue(summary, benchmarkCase, SummaryStyle.Default);
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
        {
            var statistics = summary[benchmarkCase]?.ResultStatistics;
            if (statistics?.Percentiles == null)
                return "NA";

            var nanoseconds = statistics.Percentiles.Percentile(_percentile);
            var timeUnit = style.TimeUnit ?? TimeUnit.GetBestTimeUnit(nanoseconds);
            double divisor;
            if (timeUnit == TimeUnit.Nanosecond)
                divisor = 1.0;
            else if (timeUnit == TimeUnit.Microsecond)
                divisor = 1_000.0;
            else if (timeUnit == TimeUnit.Millisecond)
                divisor = 1_000_000.0;
            else if (timeUnit == TimeUnit.Second)
                divisor = 1_000_000_000.0;
            else
                divisor = 1.0;
            return (nanoseconds / divisor).ToString("N2");
        }

        public override string ToString() => ColumnName;
    }

    private sealed class CorrectnessColumn : IColumn
    {
        public string Id => "Correctness.Failures";
        public string ColumnName => "Fails";
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => 0;
        public bool IsNumeric => false;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "Correctness failures recorded for this benchmark (0 = all operations succeeded)";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
            GetValue(summary, benchmarkCase, SummaryStyle.Default);

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
        {
            var method = benchmarkCase.Descriptor.WorkloadMethod;
            var methodName = method.Name;
            string? framework = null;
            string? scenario = null;

            // Explicit identity wins outright — see CorrectnessIdentityAttribute for why.
            var identity = method.GetCustomAttribute<CorrectnessIdentityAttribute>();
            if (identity != null)
            {
                framework = identity.Framework;
                scenario = identity.Scenario;
            }
            else if (methodName.EndsWith("_Pengdows", StringComparison.Ordinal))
            {
                framework = "Pengdows";
                scenario = methodName[..^"_Pengdows".Length];
            }
            else if (methodName.EndsWith("_Dapper", StringComparison.Ordinal))
            {
                framework = "Dapper";
                scenario = methodName[..^"_Dapper".Length];
            }
            else if (methodName.EndsWith("_EntityFramework", StringComparison.Ordinal))
            {
                framework = "EntityFramework";
                scenario = methodName[..^"_EntityFramework".Length];
            }

            if (framework == null || scenario == null)
            {
                // A blank "-" here is indistinguishable from "this class has no correctness
                // tracking at all." If the class DOES write correctness fragments but this
                // specific method's name didn't match any known suffix, that's the exact
                // silent-failure class found three times in one session (2026-08-27) — render
                // "?" and log so it can't pass for a verified zero.
                if (BenchmarkCorrectnessArtifacts.HasFragmentsFor(summary.Title))
                {
                    Console.Error.WriteLine(
                        $"[CorrectnessColumn] WARNING: '{methodName}' has correctness fragments " +
                        "for its class but no CorrectnessIdentityAttribute and no recognized name " +
                        "suffix (_Pengdows/_Dapper/_EntityFramework) — cannot resolve framework/" +
                        "scenario. Add [CorrectnessIdentity(...)] to this method.");
                    return "?";
                }

                return "-";
            }

            // Must be null (not a literal placeholder string like "No parameters") for
            // parameterless benchmarks: BenchmarkCorrectnessArtifacts.CountFailures and
            // MarkInvalid's own writer both normalize null/whitespace to the "*" wildcard key,
            // and require an exact string match between what was recorded and what's queried.
            // A literal fallback string here would never equal that wildcard, silently making
            // this column read 0 for every parameterless class regardless of what actually
            // happened — confirmed in practice on 2026-08-27: SQLiteWriteContentionBenchmarks
            // showed "Fails: 0" for Dapper/EntityFramework in this exact table while their own
            // (separately tracked, unaffected) tx-latency files recorded 512 and 456 real lost
            // transactions respectively.
            var displayInfo = benchmarkCase.DisplayInfo;
            var start = displayInfo.IndexOf('[');
            var end = displayInfo.LastIndexOf(']');
            var paramKey = (start >= 0 && end > start)
                ? displayInfo.Substring(start + 1, end - start - 1).Trim()
                : null;

            var count = BenchmarkCorrectnessArtifacts.CountFailures(
                summary.Title, paramKey, scenario, framework);

            // null means the correctness artifact was missing/unreadable — distinct from a
            // verified 0. Rendering both as "0" is exactly the bug that let a stale report claim
            // "Fails: 0" for a run whose artifact never survived BenchmarkDotNet's own cleanup.
            return count.HasValue ? count.Value.ToString() : "N/A";
        }

        public override string ToString() => ColumnName;
    }
}
