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
        var totalStart = DateTime.UtcNow;
        Output.WriteLine($"[{totalStart:HH:mm:ss.fff}] Starting test initialization...");

        await Host.StartAsync();
        Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Host started (took {(DateTime.UtcNow - totalStart).TotalMilliseconds:F0}ms)");

        var providers = GetSupportedProviders();
        var orchestrator = new ParallelTestOrchestrator(Host.Services, ShouldIncludeOracle());
        Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Testing against {providers.Count()} providers: {string.Join(", ", providers)}");

        foreach (var provider in providers)
        {
            try
            {
                var providerStart = DateTime.UtcNow;
                Output.WriteLine($"[{providerStart:HH:mm:ss.fff}] Initializing {provider} test environment...");

                var container = await orchestrator.CreateContainerAsync(provider);
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} container created (took {(DateTime.UtcNow - providerStart).TotalMilliseconds:F0}ms)");

                if (container != null)
                {
                    TestContainers[provider] = container;

                    var contextStart = DateTime.UtcNow;
                    var context = await container.GetDatabaseContextAsync(Host.Services);
                    Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} context created (took {(DateTime.UtcNow - contextStart).TotalMilliseconds:F0}ms)");

                    DatabaseContexts[provider] = context;

                    // Run any provider-specific setup
                    var setupStart = DateTime.UtcNow;
                    Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} calling SetupDatabaseAsync...");
                    await SetupDatabaseAsync(provider, context);
                    Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} setup completed (took {(DateTime.UtcNow - setupStart).TotalMilliseconds:F0}ms)");
                }
            }
            catch (Exception ex)
            {
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ❌ Failed to initialize {provider}: {ex.Message}");
                // Continue with other providers
            }
        }

        if (!DatabaseContexts.Any())
        {
            throw new InvalidOperationException("No database providers could be initialized for testing");
        }

        Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ✅ All initialization complete (total: {(DateTime.UtcNow - totalStart).TotalMilliseconds:F0}ms)");
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
        var providers = new[]
        {
            SupportedDatabase.Sqlite,
            SupportedDatabase.PostgreSql,
            SupportedDatabase.SqlServer,
            SupportedDatabase.MySql,
            SupportedDatabase.MariaDb
        };

        var only = Environment.GetEnvironmentVariable("INTEGRATION_ONLY");
        if (string.IsNullOrWhiteSpace(only))
        {
            return providers;
        }

        var filtered = new List<SupportedDatabase>();
        foreach (var token in only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<SupportedDatabase>(token, true, out var parsed))
            {
                filtered.Add(parsed);
            }
        }

        if (filtered.Count == 0)
        {
            throw new InvalidOperationException($"INTEGRATION_ONLY did not match any SupportedDatabase values: '{only}'.");
        }

        return providers.Where(filtered.Contains).ToArray();
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
        var testStart = DateTime.UtcNow;

        foreach (var (provider, context) in DatabaseContexts)
        {
            try
            {
                var providerStart = DateTime.UtcNow;
                Output.WriteLine($"[{providerStart:HH:mm:ss.fff}] ▶️ {provider} test starting");
                Output.WriteLine($"[{providerStart:HH:mm:ss.fff}] Running test against {provider}...");
                await testAction(provider, context);
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ✅ {provider} test finished");
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ✅ {provider} test completed successfully (took {(DateTime.UtcNow - providerStart).TotalMilliseconds:F0}ms)");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ❌ {provider} test failed: {ex.Message}");
                failures.Add((provider, ex));
            }
        }

        Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Test execution across all providers complete (total: {(DateTime.UtcNow - testStart).TotalMilliseconds:F0}ms)");

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
