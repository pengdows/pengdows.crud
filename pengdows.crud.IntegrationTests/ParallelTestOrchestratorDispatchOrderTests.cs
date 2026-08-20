using System.Threading;
using pengdows.crud;
using testbed;
using Xunit;

namespace pengdows.crud.IntegrationTests;

/// <summary>
/// Verifies dispatch ordering for <see cref="ParallelTestOrchestrator"/>'s fixed
/// <see cref="SemaphoreSlim"/>(2) container-startup gate. The gate itself is unchanged — it still
/// runs exactly 2 configurations at a time and backfills whichever slot frees first with the next
/// item in list order. The only lever is which order the list is in: sorting by descending
/// <see cref="TestConfiguration.StartupWeightSeconds"/> means the two heaviest/slowest-starting
/// containers (e.g. Db2, Oracle) occupy both slots from the very start, while fast ones stream
/// through the slot that frees first — instead of Db2 being queued near the end of a fixed list
/// and not even starting its slow image pull until 9 other configurations have already cycled
/// through, which is what caused a single Db2 run to dominate total wall-clock time.
/// </summary>
public sealed class ParallelTestOrchestratorDispatchOrderTests
{
    private static TestConfiguration Fake(string name, int startupWeightSeconds)
    {
        return new TestConfiguration
        {
            ContainerName = name,
            DatabaseProvider = name,
            Container = new NeverStartedTestContainer(),
            TestProviderFactory = (_, _) => throw new NotSupportedException("Not used by this test."),
            StartupWeightSeconds = startupWeightSeconds
        };
    }

    [Fact]
    public void OrderByStartupWeightDescending_SortsHeaviestFirst()
    {
        var configs = new[]
        {
            Fake("Fast", 1),
            Fake("Heaviest", 60),
            Fake("Medium", 20)
        };

        var ordered = ParallelTestOrchestrator.OrderByStartupWeightDescending(configs);

        Assert.Equal(new[] { "Heaviest", "Medium", "Fast" }, ordered.Select(c => c.ContainerName));
    }

    [Fact]
    public void OrderByStartupWeightDescending_PreservesRelativeOrderForEqualWeights()
    {
        var configs = new[]
        {
            Fake("First", 5),
            Fake("Second", 5),
            Fake("Third", 5)
        };

        var ordered = ParallelTestOrchestrator.OrderByStartupWeightDescending(configs);

        Assert.Equal(new[] { "First", "Second", "Third" }, ordered.Select(c => c.ContainerName));
    }

    [Fact]
    public void GetTestConfigurations_Db2HasTheHighestStartupWeight()
    {
        // Db2's own container startup has been directly observed in this project's live testbed
        // runs to take 45-70s — by far the slowest of any always-on database — so it must sort
        // first, occupying a dispatch slot from t=0 rather than waiting behind the fixed list's
        // 9 other entries under the unordered SemaphoreSlim(2) FIFO dispatch.
        var orchestrator = new ParallelTestOrchestrator(NullServiceProvider.Instance);
        var configs = orchestrator.GetTestConfigurations();

        var ordered = ParallelTestOrchestrator.OrderByStartupWeightDescending(configs);

        Assert.Equal("Db2", ordered[0].ContainerName);
    }

    private sealed class NeverStartedTestContainer : ITestContainer
    {
        public Task StartAsync() => throw new NotSupportedException("Not used by this test.");

        public Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services) =>
            throw new NotSupportedException("Not used by this test.");

        public Task RunTestWithContainerAsync<TTestProvider>(
            IServiceProvider services,
            Func<IDatabaseContext, IServiceProvider, TTestProvider> testProviderFactory)
            where TTestProvider : TestProvider =>
            throw new NotSupportedException("Not used by this test.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
