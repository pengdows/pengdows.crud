using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.enums;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for database integration tests that provides test infrastructure
/// for running individual tests against multiple database providers.
/// </summary>
public abstract class DatabaseTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected readonly IntegrationTestFixture Fixture;
    protected Dictionary<SupportedDatabase, IDatabaseContext> DatabaseContexts = new();

    protected DatabaseTestBase(ITestOutputHelper output, IntegrationTestFixture fixture)
    {
        Output = output;
        Fixture = fixture;
    }

    public virtual async Task InitializeAsync()
    {
        var totalStart = DateTime.UtcNow;
        Output.WriteLine($"[{totalStart:HH:mm:ss.fff}] Starting test initialization...");

        var requestedProviders = GetSupportedProviders().ToList();
        Output.WriteLine(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] Testing against {requestedProviders.Count} providers: {string.Join(", ", requestedProviders)}");

        var contexts = new Dictionary<SupportedDatabase, IDatabaseContext>();

        foreach (var provider in requestedProviders)
        {
            try
            {
                var context = await Fixture.CreateDatabaseContextAsync(provider);
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} connection string: {context.ConnectionString}");
                contexts[provider] = context;
            }
            catch (Exception ex)
            {
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] ⚠️ {provider} is not available for testing: {ex.Message}");
            }
        }

        if (!contexts.Any())
        {
            throw new InvalidOperationException("No database providers could be initialized for testing");
        }

        DatabaseContexts = contexts;

        foreach (var (provider, context) in DatabaseContexts)
        {
            var setupStart = DateTime.UtcNow;
            Output.WriteLine($"[{setupStart:HH:mm:ss.fff}] Resetting {provider} database...");
            await CleanupDatabaseAsync(provider, context);
            Output.WriteLine(
                $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} cleanup complete, running SetupDatabaseAsync...");
            await SetupDatabaseAsync(provider, context);
            Output.WriteLine(
                $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} setup completed (took {(DateTime.UtcNow - setupStart).TotalMilliseconds:F0}ms)");
        }

        Output.WriteLine(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] ✅ All initialization complete (total: {(DateTime.UtcNow - totalStart).TotalMilliseconds:F0}ms)");
    }

    public virtual Task DisposeAsync()
    {
        DatabaseContexts.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Override to specify which database providers this test should run against.
    /// Default is all supported providers.
    /// </summary>
    protected virtual IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        var providers = IntegrationTestConfiguration.EnabledProviders;

        var only = Environment.GetEnvironmentVariable("INTEGRATION_ONLY");
        if (string.IsNullOrWhiteSpace(only))
        {
            return providers;
        }

        var filtered = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Enum.TryParse<SupportedDatabase>(token, true, out var parsed)
                ? parsed
                : (SupportedDatabase?)null)
            .Where(parsed => parsed.HasValue)
            .Select(parsed => parsed!.Value)
            .ToList();

        if (filtered.Count == 0)
        {
            throw new InvalidOperationException(
                $"INTEGRATION_ONLY did not match any SupportedDatabase values: '{only}'.");
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

    protected virtual Task CleanupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        return DatabaseSchemaHelper.DropTablesAsync(context);
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
                Output.WriteLine(
                    $"[{providerStart:HH:mm:ss.fff}] {provider} connections before test: {context.NumberOfOpenConnections} open, peak {context.PeakOpenConnections}");
                await testAction(provider, context);
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ✅ {provider} test finished");
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} connections after test: {context.NumberOfOpenConnections} open, peak {context.PeakOpenConnections}");
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] ✅ {provider} test completed successfully (took {(DateTime.UtcNow - providerStart).TotalMilliseconds:F0}ms)");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ❌ {provider} test failed: {ex.Message}");
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} connections at failure: {context.NumberOfOpenConnections} open, peak {context.PeakOpenConnections}");
                failures.Add((provider, ex));
            }
        }

        Output.WriteLine(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] Test execution across all providers complete (total: {(DateTime.UtcNow - testStart).TotalMilliseconds:F0}ms)");

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
    protected async Task RunTestAgainstProviderAsync(SupportedDatabase provider,
        Func<IDatabaseContext, Task> testAction)
    {
        if (!DatabaseContexts.TryGetValue(provider, out var context))
        {
            throw new InvalidOperationException($"Provider {provider} is not available for testing");
        }

        await testAction(context);
    }

    protected IAuditValueResolver GetAuditResolver()
    {
        return Fixture.Services.GetService<IAuditValueResolver>()
               ?? new StringAuditContextProvider();
    }

    protected static async Task DropTableIfExistsAsync(IDatabaseContext context, string tableName)
    {
        var wrappedTable = context.WrapObjectName(tableName);
        await using var container = context.CreateSqlContainer($"DROP TABLE {wrappedTable}");
        try
        {
            await container.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTableMissingException(ex))
        {
            // Table was not present; swallow
        }
    }

    protected Task<IDatabaseContext> CreateAdditionalContextAsync(SupportedDatabase provider)
    {
        return Fixture.CreateAdditionalContextAsync(provider);
    }

    private static bool IsTableMissingException(Exception ex)
    {
        var message = ex.Message?.ToLowerInvariant() ?? string.Empty;
        return message.Contains("does not exist")
               || message.Contains("doesn't exist")
               || message.Contains("no such table")
               || message.Contains("table with name")
               || message.Contains("catalog error")
               || message.Contains("table unknown")
               || message.Contains("unknown table")
               || message.Contains("table not found")
               || message.Contains("invalid object name")
               || message.Contains("ora-00942");
    }
}
