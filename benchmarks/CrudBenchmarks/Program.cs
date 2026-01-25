using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace CrudBenchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var config = ShouldUseInProcess()
            ? new InProcessConfig()
            : null;

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
            AddJob(Job.Default
                .WithToolchain(InProcessNoEmitToolchain.Instance)
                .WithId("InProcess"));
        }
    }
}