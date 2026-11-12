using System.Linq;
using CrudBenchmarks;
using Xunit;

namespace CrudBenchmarks.Tests;

// The ratio report must show three EF tiers side by side: the raw-SQL arm (same statement as
// Dapper, the equal-footing cell), the idiomatic LINQ arm, and the compiled-query arm (the most
// optimized EF path). Dropping the LINQ or compiled arms silently hides the way EF is normally written.
public sealed class CrossFrameworkRatioWriterTests
{
    [Theory]
    [InlineData("ReadSingle_EntityFramework", "ReadSingle", "EntityFramework")]
    [InlineData("ReadSingle_EntityFramework_Linq", "ReadSingle", "EntityFrameworkLinq")]
    [InlineData("ReadSingle_EntityFramework_Compiled", "ReadSingle", "EntityFrameworkCompiled")]
    [InlineData("ReadList_Pengdows", "ReadList", "Pengdows")]
    [InlineData("Create_Dapper", "Create", "Dapper")]
    public void TrySplitMethod_RecognizesEveryEntityFrameworkTier(string methodName, string scenario, string framework)
    {
        Assert.True(CrossFrameworkRatioWriter.TrySplitMethod(methodName, out var actualScenario, out var actualFramework));
        Assert.Equal(scenario, actualScenario);
        Assert.Equal(framework, actualFramework.ToString());
    }

    [Fact]
    public void TrySplitMethod_LeavesPooledAndApiArmsOutOfTheReport()
    {
        Assert.False(CrossFrameworkRatioWriter.TrySplitMethod("ReadSingle_EntityFramework_Pooled", out _, out _));
        Assert.False(CrossFrameworkRatioWriter.TrySplitMethod("ReadSingle_Pengdows_RetrieveOneAsync", out _, out _));
    }

    [Fact]
    public void BuildMarkdown_ShowsRawSqlLinqAndCompiledEfTiersWithTheirRatios()
    {
        var row = new CrossFrameworkRatioWriter.CrossRatioRow(
            ParameterKey: "No parameters",
            Scenario: "ReadSingle",
            PengdowsMeanNs: 100_000,
            DapperMeanNs: 100_000,
            EntityFrameworkMeanNs: 150_000,
            EntityFrameworkLinqMeanNs: 300_000,
            EntityFrameworkCompiledMeanNs: 120_000,
            PengdowsOverDapper: 1.0,
            EntityFrameworkOverPengdows: 1.5,
            EntityFrameworkLinqOverPengdows: 3.0,
            EntityFrameworkCompiledOverPengdows: 1.2);

        var markdown = CrossFrameworkRatioWriter.BuildMarkdown("Title", new[] { row });

        Assert.Contains("EF raw SQL ÷P", markdown);
        Assert.Contains("EF LINQ ÷P", markdown);
        Assert.Contains("EF compiled ÷P", markdown);
        Assert.Contains("| 3.000 |", markdown);
        Assert.Contains("| 1.200 |", markdown);
    }

    [Fact]
    public void BuildMarkdown_ShowsDashWhenATierIsMissing()
    {
        var row = new CrossFrameworkRatioWriter.CrossRatioRow(
            "No parameters", "ReadSingle", 100_000, 100_000,
            EntityFrameworkMeanNs: null,
            EntityFrameworkLinqMeanNs: null,
            EntityFrameworkCompiledMeanNs: null,
            PengdowsOverDapper: 1.0,
            EntityFrameworkOverPengdows: null,
            EntityFrameworkLinqOverPengdows: null,
            EntityFrameworkCompiledOverPengdows: null);

        var markdown = CrossFrameworkRatioWriter.BuildMarkdown("Title", new[] { row });

        Assert.Contains("| - | - | - | 1.000 | - | - | - |", markdown);
    }
}
