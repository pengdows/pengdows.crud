#region

using System;
using System.IO;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class PerformanceOptimizationBenchmarksTests
{
    [Fact]
    public void TestEntity_DefinesPrimaryKey_ForRetrieveOneBaseline()
    {
        var path = GetBenchmarkSourcePath();
        var contents = File.ReadAllText(path);

        var start = contents.IndexOf("public class TestEntity", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate TestEntity definition in PerformanceOptimizationBenchmarks.cs.");

        var end = contents.IndexOf("// ============================================================================", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = contents.Length;
        }

        var testEntityBlock = contents.Substring(start, end - start);
        Assert.Contains("[PrimaryKey", testEntityBlock, StringComparison.Ordinal);
    }

    private static string GetBenchmarkSourcePath()
    {
        var root = GetRepoRoot();
        return Path.Combine(root, "benchmarks", "CrudBenchmarks", "Internal", "PerformanceOptimizationBenchmarks.cs");
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

        throw new DirectoryNotFoundException("Could not locate repository root for benchmark source validation.");
    }
}
