using Microsoft.Extensions.DependencyInjection;
using testbed;
using Xunit;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// INTEGRATION_ONLY / TESTBED_ONLY name databases the way SupportedDatabase does ("SapHana",
/// "SqlServer"), while testbed configurations carry display names ("SAP HANA", "SQL Server").
/// Matching ignored the difference, so IntegrationMatrixTests ran nothing with
/// INTEGRATION_ONLY=SapHana and failed on an empty result (HARN-011).
/// </summary>
public sealed class ParallelTestOrchestratorFilterTests
{
    private static ParallelTestOrchestrator Orchestrator() =>
        new(new ServiceCollection().BuildServiceProvider(), includeSapHana: true);

    [Theory]
    [InlineData("SapHana", "SAP HANA")]
    [InlineData("SqlServer", "SQL Server")]
    [InlineData("sap hana", "SAP HANA")]
    public void Only_SupportedDatabaseStyleName_SelectsTheDisplayNamedConfiguration(string only, string provider)
    {
        var configurations = Orchestrator().GetTestConfigurations(new HashSet<string> { only });

        Assert.NotEmpty(configurations);
        Assert.All(configurations, c => Assert.Equal(provider, c.DatabaseProvider));
    }

    [Fact]
    public void Exclude_SupportedDatabaseStyleName_RemovesTheDisplayNamedConfiguration()
    {
        var configurations = Orchestrator().GetTestConfigurations(exclude: new HashSet<string> { "SapHana" });

        Assert.DoesNotContain(configurations, c => c.DatabaseProvider == "SAP HANA");
    }
}
