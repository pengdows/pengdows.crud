using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using testbed;
using System.Runtime.CompilerServices;
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
    private IAuditValueResolver? _cachedAuditResolver;

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

        var enabledProviders = IntegrationTestConfiguration.EnabledProviders;
        var contexts = new Dictionary<SupportedDatabase, IDatabaseContext>();
        var skipReasons = new List<string>();

        foreach (var provider in requestedProviders)
        {
            // Log and skip providers that were filtered out before containers were started
            if (!enabledProviders.Contains(provider))
            {
                var exclusionReason = BuildExclusionReason(provider);
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] ⚠️ {provider} excluded from this test run: {exclusionReason}");
                skipReasons.Add($"{provider}: {exclusionReason}");
                continue;
            }

            try
            {
                IntegrationTraceLog.Write(provider, "context acquisition start", Output);
                var context = await Fixture.CreateDatabaseContextAsync(provider);
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} connection string: {context.ConnectionString}");
                IntegrationTraceLog.Write(provider, "context acquisition done", Output);
                contexts[provider] = context;
            }
            catch (Exception ex)
            {
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] ⚠️ {provider} is not available for testing: {ex.Message}");
                skipReasons.Add($"{provider}: {ex.Message}");
            }
        }

        if (!contexts.Any())
        {
            var testClass = GetType().Name;
            var requested = requestedProviders.Count == 0
                ? "none (check INTEGRATION_ONLY env var or GetSupportedProviders override)"
                : string.Join(", ", requestedProviders);
            var reasonDetail = skipReasons.Count > 0
                ? $" Skipped because: {string.Join("; ", skipReasons)}."
                : string.Empty;
            throw new Xunit.SkipException(
                $"{testClass} requires [{requested}] but none could be initialized.{reasonDetail}");
        }

        DatabaseContexts = contexts;

        foreach (var (provider, context) in DatabaseContexts)
        {
            var setupStart = DateTime.UtcNow;
            Output.WriteLine($"[{setupStart:HH:mm:ss.fff}] Resetting {provider} database...");
            IntegrationTraceLog.Write(provider, "cleanup start", Output);
            await CleanupDatabaseAsync(provider, context);
            IntegrationTraceLog.Write(provider,
                $"cleanup done elapsedMs={(DateTime.UtcNow - setupStart).TotalMilliseconds:F0}", Output);
            Output.WriteLine(
                $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} cleanup complete, running SetupDatabaseAsync...");
            IntegrationTraceLog.Write(provider, "setup start", Output);
            await SetupDatabaseAsync(provider, context);
            IntegrationTraceLog.Write(provider,
                $"setup done elapsedMs={(DateTime.UtcNow - setupStart).TotalMilliseconds:F0}", Output);
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
    protected async Task RunTestAgainstAllProvidersAsync(
        Func<SupportedDatabase, IDatabaseContext, Task> testAction,
        [CallerMemberName] string? testName = null)
    {
        var failures = new List<(SupportedDatabase Provider, Exception Error)>();
        var testStart = DateTime.UtcNow;

        foreach (var (provider, context) in DatabaseContexts)
        {
            try
            {
                var providerStart = DateTime.UtcNow;
                IntegrationTraceLog.Write(provider, $"test start name={testName ?? "<unknown>"}", Output);
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
                IntegrationTraceLog.Write(provider,
                    $"test done name={testName ?? "<unknown>"} elapsedMs={(DateTime.UtcNow - providerStart).TotalMilliseconds:F0}",
                    Output);
            }
            catch (Exception ex)
            {
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] ❌ {provider} test failed: {ex.Message}");
                Output.WriteLine(
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} connections at failure: {context.NumberOfOpenConnections} open, peak {context.PeakOpenConnections}");
                IntegrationTraceLog.Write(provider,
                    $"test fail name={testName ?? "<unknown>"} error={ex.Message}",
                    Output);
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
    protected async Task RunTestAgainstProviderAsync(
        SupportedDatabase provider,
        Func<IDatabaseContext, Task> testAction,
        [CallerMemberName] string? testName = null)
    {
        if (!DatabaseContexts.TryGetValue(provider, out var context))
        {
            throw new InvalidOperationException($"Provider {provider} is not available for testing");
        }

        var start = DateTime.UtcNow;
        IntegrationTraceLog.Write(provider, $"test start name={testName ?? "<unknown>"}", Output);
        await testAction(context);
        IntegrationTraceLog.Write(provider,
            $"test done name={testName ?? "<unknown>"} elapsedMs={(DateTime.UtcNow - start).TotalMilliseconds:F0}",
            Output);
    }

    protected IAuditValueResolver GetAuditResolver()
    {
        return _cachedAuditResolver ??= (Fixture.Services.GetService<IAuditValueResolver>()
                                         ?? new StringAuditContextProvider());
    }

    protected static async Task DropTableIfExistsAsync(IDatabaseContext context, string tableName)
    {
        var wrappedTable = IntegrationObjectNameHelper.Table(context, tableName);
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

    /// <summary>
    /// Returns true for providers that enforce read-only at the transaction level.
    /// Delegates to <see cref="ISqlDialect.SupportsReadOnlyTransactions"/> so each dialect
    /// self-reports its capability rather than maintaining a hardcoded list here.
    /// Note: Oracle is excluded at the dialect level because its read-only transactions pin a
    /// consistent snapshot, which is incompatible with the per-test DDL reset path (ORA-01466).
    /// </summary>
    protected static bool SupportsReadOnlyTransactions(IDatabaseContext context) =>
        context.Dialect.SupportsReadOnlyTransactions;

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

    private static string BuildExclusionReason(SupportedDatabase provider)
    {
        var integrationOnly = Environment.GetEnvironmentVariable("INTEGRATION_ONLY");
        if (!string.IsNullOrWhiteSpace(integrationOnly))
        {
            return $"INTEGRATION_ONLY={integrationOnly} is set; this provider is not included in the filter";
        }

        if (provider == SupportedDatabase.Snowflake && !IntegrationTestConfiguration.ShouldIncludeSnowflake)
        {
            return "requires INCLUDE_SNOWFLAKE=true to enable Snowflake tests";
        }

        return "provider is not in the enabled list for this test run";
    }

}
