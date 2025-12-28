using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using pengdows.crud;
using pengdows.crud.enums;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for database integration tests that provides test infrastructure
/// for running individual tests against multiple database providers.
/// </summary>
public abstract class DatabaseTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected readonly IHost Host;
    protected readonly Dictionary<SupportedDatabase, IDatabaseContext> DatabaseContexts = new();
    protected readonly Dictionary<SupportedDatabase, ITestContainer> TestContainers = new();

    protected DatabaseTestBase(ITestOutputHelper output)
    {
        Output = output;

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();
        Host = builder.Build();
    }

    public virtual async Task InitializeAsync()
    {
        await Host.StartAsync();

        var providers = GetSupportedProviders();
        var orchestrator = new ParallelTestOrchestrator(Host.Services, ShouldIncludeOracle());

        foreach (var provider in providers)
        {
            try
            {
                Output.WriteLine($"Initializing {provider} test environment...");

                var container = await orchestrator.CreateContainerAsync(provider);
                if (container != null)
                {
                    TestContainers[provider] = container;
                    var context = await container.GetDatabaseContextAsync(Host.Services);
                    DatabaseContexts[provider] = context;

                    // Run any provider-specific setup
                    await SetupDatabaseAsync(provider, context);
                }
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Failed to initialize {provider}: {ex.Message}");
                // Continue with other providers
            }
        }

        if (!DatabaseContexts.Any())
        {
            throw new InvalidOperationException("No database providers could be initialized for testing");
        }
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var context in DatabaseContexts.Values)
        {
            context.Dispose();
        }

        foreach (var container in TestContainers.Values)
        {
            await container.DisposeAsync();
        }

        await Host.StopAsync();
        Host.Dispose();
    }

    /// <summary>
    /// Override to specify which database providers this test should run against.
    /// Default is all supported providers.
    /// </summary>
    protected virtual IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return new[]
        {
            SupportedDatabase.Sqlite,
            SupportedDatabase.PostgreSql,
            SupportedDatabase.SqlServer,
            SupportedDatabase.MySql,
            SupportedDatabase.MariaDb
        };
    }

    /// <summary>
    /// Override to perform database-specific setup (create tables, etc.)
    /// </summary>
    protected virtual Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run a test against all configured database providers
    /// </summary>
    protected async Task RunTestAgainstAllProvidersAsync(Func<SupportedDatabase, IDatabaseContext, Task> testAction)
    {
        var failures = new List<(SupportedDatabase Provider, Exception Error)>();

        foreach (var (provider, context) in DatabaseContexts)
        {
            try
            {
                Output.WriteLine($"Running test against {provider}...");
                await testAction(provider, context);
                Output.WriteLine($"✅ {provider} test completed successfully");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"❌ {provider} test failed: {ex.Message}");
                failures.Add((provider, ex));
            }
        }

        if (failures.Any())
        {
            var errorMessage = string.Join("\n", failures.Select(f => $"{f.Provider}: {f.Error.Message}"));
            throw new AggregateException($"Test failed on {failures.Count} provider(s):\n{errorMessage}",
                failures.Select(f => f.Error));
        }
    }

    /// <summary>
    /// Run a test against a specific database provider
    /// </summary>
    protected async Task RunTestAgainstProviderAsync(SupportedDatabase provider, Func<IDatabaseContext, Task> testAction)
    {
        if (!DatabaseContexts.TryGetValue(provider, out var context))
        {
            throw new InvalidOperationException($"Provider {provider} is not available for testing");
        }

        await testAction(context);
    }

    protected static bool ShouldIncludeOracle() => string.Equals(
        Environment.GetEnvironmentVariable("INCLUDE_ORACLE"),
        "true",
        StringComparison.OrdinalIgnoreCase);
}