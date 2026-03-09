using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace OrderedDictBench;

public class Program
{
    public static void Main(string[] args)
    {
        var config = ManualConfig.CreateEmpty()
            .WithArtifactsPath(Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkDotNet.Artifacts"))
            .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
            .AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub)
            .AddExporter(BenchmarkDotNet.Exporters.Csv.CsvExporter.Default)
            .AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
