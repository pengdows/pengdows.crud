using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

public class BenchmarkFairnessTests
{
    [Fact]
    public void Benchmarks_DoNotApplySessionSettingsFixups()
    {
        var tokens = new[]
        {
            "BenchmarkSessionSettings.ApplyAsync",
            "SqlServerSessionSettings.ApplyAsync",
            "SessionSettingsConnectionInterceptor"
        };

        var offenders = FindOffenders(tokens, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BenchmarkSessionSettings.cs",
            "SqlServerSessionSettings.cs"
        });

        Assert.True(offenders.Count == 0,
            $"Session settings fix-ups found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Benchmarks_DoNotContainSessionSettingsVariants()
    {
        var tokens = new[] { "WithSessionSettings", "WithSessionMgmt" };
        var offenders = FindOffenders(tokens, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(offenders.Count == 0,
            $"Session settings benchmark variants found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Benchmarks_DoNotContainIndexedViewHints()
    {
        var tokens = new[] { "NOEXPAND" };
        var offenders = FindOffenders(tokens, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(offenders.Count == 0,
            $"Indexed view hints found in: {string.Join(", ", offenders)}");
    }

    private static List<string> FindOffenders(IEnumerable<string> tokens, HashSet<string> excludedFiles)
    {
        var benchmarksDir = GetBenchmarksDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(benchmarksDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (excludedFiles.Contains(fileName))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (tokens.Any(token => text.Contains(token, StringComparison.Ordinal)))
            {
                offenders.Add(fileName);
            }
        }

        return offenders;
    }

    private static string GetBenchmarksDirectory()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(root, "benchmarks", "CrudBenchmarks");
    }

    private static string LoadBenchmarkText(string fileName)
    {
        var path = Path.Combine(GetBenchmarksDirectory(), fileName);
        return File.ReadAllText(path);
    }

    private static void AssertAllPresent(string fileName, string text, IEnumerable<string> tokens)
    {
        var missing = tokens.Where(token => !text.Contains(token, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0,
            $"{fileName} missing: {string.Join(", ", missing)}");
    }
}
