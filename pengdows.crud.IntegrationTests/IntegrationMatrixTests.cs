using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests;

public sealed class IntegrationMatrixTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly IHost _host;
    private ParallelTestOrchestrator? _orchestrator;

    public IntegrationMatrixTests(ITestOutputHelper output)
    {
        _output = output;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();
        _host = builder.Build();
    }

    [Fact]
    public async Task All_supported_providers_complete_successfully()
    {
        var orchestrator = _orchestrator ?? throw new InvalidOperationException("Test host not initialized");

        var only = ParseList(Environment.GetEnvironmentVariable("TESTBED_ONLY"));
        var exclude = ParseList(Environment.GetEnvironmentVariable("TESTBED_EXCLUDE"));

        var results = await orchestrator.RunAllTestsAsync(only, exclude);
        Assert.NotEmpty(results);

        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                _output.WriteLine(
                    $"Provider {failure.DatabaseProvider} ({failure.ContainerName}) failed: {failure.Error}");
            }

            Assert.Fail("One or more integration providers failed. See test output for details.");
        }
    }

    public async Task InitializeAsync()
    {
        await _host.StartAsync();
        _orchestrator = new ParallelTestOrchestrator(_host.Services, ShouldIncludeOracle());
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private static bool ShouldIncludeOracle()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("INCLUDE_ORACLE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static ISet<string> ParseList(string? csv)
    {
        return string.IsNullOrWhiteSpace(csv)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}