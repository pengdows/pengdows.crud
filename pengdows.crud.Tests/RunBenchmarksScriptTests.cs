using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

public class RunBenchmarksScriptLegacyTests
{

    [Fact]
    public void RunBenchmarksScript_PrefersExistingCompiledBenchmarkBinary()
    {
        var scriptPath = GetScriptPath();
        var contents = File.ReadAllText(scriptPath);

        // CrudBenchmarks is multi-targeted (net8.0;net10.0): the compiled binary lives under the
        // framework folder chosen by BENCH_FRAMEWORK (default net10.0).
        Assert.Contains("bench_tfm=\"${BENCH_FRAMEWORK:-net10.0}\"", contents, StringComparison.Ordinal);
        Assert.Contains("benchmarks/CrudBenchmarks/bin/Release/${bench_tfm}", contents, StringComparison.Ordinal);
        Assert.Contains("CrudBenchmarks", contents, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetScriptPath()
    {
        var root = GetRepoRoot();
        return Path.Combine(root, "run-benchmarks.sh");
    }

    private static string GetRepoRoot()
    {
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = start; current != null; current = current.Parent)
        {
            var slnPath = Path.Combine(current.FullName, "pengdows.crud.sln");
            if (File.Exists(slnPath))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for benchmark script validation.");
    }
}
