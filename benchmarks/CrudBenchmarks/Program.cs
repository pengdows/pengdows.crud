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
            AddLogger(ConsoleLogger.Default);
            AddColumnProvider(DefaultColumnProviders.Instance);
            AddColumn(StatisticColumn.P95);
            AddColumn(new PercentileColumn("P99", 99));
            AddJob(Job.Default
                .WithToolchain(InProcessNoEmitToolchain.Instance)
                .WithId("InProcess"));
        }
    }

    private sealed class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddLogger(ConsoleLogger.Default);
            AddColumnProvider(DefaultColumnProviders.Instance);
            AddColumn(StatisticColumn.P95);
            AddColumn(new PercentileColumn("P99", 99));
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
}
