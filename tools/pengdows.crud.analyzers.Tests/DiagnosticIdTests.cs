using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace pengdows.crud.analyzers.Tests;

/// <summary>
/// The analyzer project is the same code on 2.0.x and 3.0, so every rule is reported on both and
/// each needs its own ID. PGC027 was used by two different rules (the 2.0.x compatibility-surface
/// rule and 3.0's multitenancy call-site rule); both moved to new IDs so a suppression or
/// .editorconfig entry means the same rule on either branch.
/// </summary>
public sealed class DiagnosticIdTests
{
    [Fact]
    public void CompatibilityLeakAnalyzer_UsesPGC028()
    {
        Assert.Equal("PGC028", CompatibilityLeakAnalyzer.DiagnosticId);
    }

    [Fact]
    public void GatewayCallSiteContextAnalyzer_UsesPGC029()
    {
        Assert.Equal("PGC029", GatewayCallSiteContextAnalyzer.DiagnosticId);
    }

    [Fact]
    public void EveryRuleInTheAssembly_HasItsOwnId()
    {
        var descriptors = typeof(CompatibilityLeakAnalyzer).Assembly.GetTypes()
            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => ((DiagnosticAnalyzer)Activator.CreateInstance(t)!).SupportedDiagnostics
                .Select(d => (Analyzer: t.Name, d.Id)))
            .ToArray();

        var duplicates = descriptors.GroupBy(d => d.Id)
            .Where(g => g.Select(d => d.Analyzer).Distinct().Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(d => d.Analyzer).Distinct())}")
            .ToArray();

        Assert.True(duplicates.Length == 0, "Diagnostic IDs shared by more than one analyzer: " + string.Join("; ", duplicates));
    }
}
