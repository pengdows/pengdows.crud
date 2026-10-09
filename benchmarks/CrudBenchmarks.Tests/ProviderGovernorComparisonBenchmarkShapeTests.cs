using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

public sealed class ProviderGovernorComparisonBenchmarkShapeTests
{
    [Fact]
    public void PostgreSqlComparisonHasOnlyTheTwoGovernorArms()
    {
        var methods = typeof(PostgreSqlGovernorComparisonBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<BenchmarkAttribute>() != null)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "AdaptiveGovernor", "SemaphoreGovernor" }, methods);
        Assert.True(PostgreSqlGovernorComparisonBenchmarks.ServerMaxConnections >
            PostgreSqlGovernorComparisonBenchmarks.GovernorCapacity);
    }

    [Fact]
    public void SqlServerComparisonHasOnlyTheTwoGovernorArms()
    {
        var methods = typeof(SqlServerGovernorComparisonBenchmarks)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<BenchmarkAttribute>() != null)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "AdaptiveGovernor", "SemaphoreGovernor" }, methods);
        Assert.True(SqlServerGovernorComparisonBenchmarks.ServerMaxConnections >
            SqlServerGovernorComparisonBenchmarks.GovernorCapacity);
        Assert.Equal("gov_test", SqlServerGovernorComparisonBenchmarks.DatabaseName);
    }

    [Fact]
    public void ProviderComparisonsDoNotAddASecondJobToTheInProcessHarness()
    {
        Assert.Null(typeof(PostgreSqlGovernorComparisonBenchmarks)
            .GetCustomAttribute<SimpleJobAttribute>());
        Assert.Null(typeof(SqlServerGovernorComparisonBenchmarks)
            .GetCustomAttribute<SimpleJobAttribute>());
    }

    [Fact]
    public void ProviderComparisonsUseAnOversizedClientPoolInsteadOfDisablingPooling()
    {
        var postgres = PostgreSqlGovernorComparisonBenchmarks.BuildComparisonConnectionString(
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=p");
        var sqlServer = SqlServerGovernorComparisonBenchmarks.BuildComparisonConnectionString(
            "Server=localhost,1433;Database=test;User Id=sa;Password=p");

        Assert.Contains("Pooling=true", postgres, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Maximum Pool Size=100", postgres, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pooling=true", sqlServer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Max Pool Size=100", sqlServer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SemaphoreArmsUseAnExplicitSemaphoreSlimBackedPoolGovernor()
    {
        foreach (var fileName in new[]
                 {
                     "ProviderGovernorComparisonBenchmarks.cs",
                     "FullPoolGovernorComparisonBenchmarks.cs",
                     "FullPoolGovernorContentionBenchmarks.cs",
                     "SQLiteGovernorContentionComparisonBenchmarks.cs"
                 })
        {
            var source = File.ReadAllText(FindBenchmarkSource(fileName));
            Assert.Contains("new SemaphoreSlim", source, StringComparison.Ordinal);
            Assert.Contains("sharedSemaphore:", source, StringComparison.Ordinal);
        }
    }

    private static string FindBenchmarkSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "..", "..", "..", "..", "CrudBenchmarks", fileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(fileName);
    }
}
