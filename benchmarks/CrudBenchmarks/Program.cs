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
        IConfig config = ShouldUseInProcess()
            ? new InProcessConfig()
            : new BenchmarkConfig();

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
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
            var divisor = timeUnit == TimeUnit.Nanosecond ? 1.0
                : timeUnit == TimeUnit.Microsecond ? 1_000.0
                : timeUnit == TimeUnit.Millisecond ? 1_000_000.0
                : timeUnit == TimeUnit.Second ? 1_000_000_000.0
                : 1.0;
            return (nanoseconds / divisor).ToString("N2");
        }

        public override string ToString() => ColumnName;
    }
}
