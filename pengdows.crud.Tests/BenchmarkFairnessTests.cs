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

    [Fact]
    public void ApplesToApplesBenchmarks_UsePrebuiltSqlAndFactoryConnections()
    {
        const string fileName = "ApplesToApplesDapperBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "BuildSingleReadSql",
            "_pengdowsSql",
            "_dapperSql",
            "CreateSqlContainer(_pengdowsSql)",
            "CreateConnection",
            "ConnectionString",
            "OpenAsync",
            "QuerySingleOrDefaultAsync"
        });
    }

    [Fact]
    public void ApplesToApplesBenchmarks_UseSqliteProvider()
    {
        const string fileName = "ApplesToApplesDapperBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "SqliteConnection",
            "SqliteFactory",
            "Microsoft.Data.Sqlite",
            "Mode=Memory",
            "Cache=Shared"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "DuckDBClientFactory",
            "DuckDBConnection",
            ".duckdb"
        });
    }

    [Fact]
    public void ApplesToApplesBenchmarks_ExposeFieldTypeDiagnostics()
    {
        const string fileName = "ApplesToApplesDapperBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "DumpFieldTypes",
            "GetFieldType",
            "Console.WriteLine"
        });
    }

    [Fact]
    public void SqliteDateHandlingBenchmarks_ExposeFieldTypeDiagnostics()
    {
        var fileName = Path.Combine("Internal", "SqliteDateHandlingBenchmarks.cs");
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "SqliteDateHandlingBenchmarks",
            "DumpFieldTypes",
            "GetFieldType",
            "SqliteConnection"
        });
    }

    [Fact]
    public void SqliteDateHandlingBenchmarks_IsNotSealed()
    {
        var fileName = Path.Combine("Internal", "SqliteDateHandlingBenchmarks.cs");
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "public class SqliteDateHandlingBenchmarks"
        });

        AssertAllAbsent(fileName, text, new[]
        {
            "sealed class SqliteDateHandlingBenchmarks"
        });
    }

    [Fact]
    public void TypeHandlingBenchmarks_ExposeDateTimeStringCoercion()
    {
        var fileName = Path.Combine("Internal", "TypeHandlingBenchmarks.cs");
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "Coercion_DateTime_Read_String",
            "ResolveCoercer",
            "DateTime"
        });
    }

    [Fact]
    public void BenchmarkProgram_UsesExplicitBenchmarkDiscovery()
    {
        const string fileName = "Program.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "BenchmarkAttribute",
            "FromTypes",
            "GetTypes"
        });
    }

    [Fact]
    public void BenchmarkProgram_SupportsOptInBenchmarks()
    {
        const string fileName = "Program.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "OptInBenchmarkAttribute",
            "IsOptInBenchmarkEnabled",
            "--include-opt-in",
            "CRUD_BENCH_INCLUDE_OPT_IN"
        });
    }

    [Fact]
    public void OptInBenchmarkAttribute_IsDefined()
    {
        const string fileName = "OptInBenchmarkAttribute.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "AttributeUsage(AttributeTargets.Class",
            "public sealed class OptInBenchmarkAttribute : Attribute"
        });
    }

    [Fact]
    public void MySqlDefaultConcurrencyBenchmarks_DefinesThreeScenarios()
    {
        const string fileName = "MySqlDefaultConcurrencyBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "[OptInBenchmark]",
            "public class MySqlDefaultConcurrencyBenchmarks",
            "public async Task ReadOnly_Pengdows_MySql()",
            "public async Task WriteOnly_Pengdows_MySql()",
            "public async Task RandomMix_Pengdows_MySql()"
        });
    }

    [Fact]
    public void MySqlDefaultConcurrencyBenchmarks_UsesDefaultMySqlContainerAndProvider()
    {
        const string fileName = "MySqlDefaultConcurrencyBenchmarks.cs";
        var text = LoadBenchmarkText(fileName);

        AssertAllPresent(fileName, text, new[]
        {
            "mysql:8.0",
            "MySqlClientFactory.Instance",
            "[Params(32, 64, 128, 256)]",
            "RunConcurrentWithErrors"
        });
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

    private static void AssertAllAbsent(string fileName, string text, IEnumerable<string> tokens)
    {
        var present = tokens.Where(token => text.Contains(token, StringComparison.Ordinal)).ToList();
        Assert.True(present.Count == 0,
            $"{fileName} should not contain: {string.Join(", ", present)}");
    }
}
