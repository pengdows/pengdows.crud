using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
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
        // Delegates to DatabaseSchemaHelper.TryDropTableAsync, which already recovers from two
        // known provider-specific failure shapes: Spanner's refusal to drop a table that still
        // has a secondary index (drops the blocking index first, then retries) and Firebird's
        // DDL-vs-connection-pooling metadata lock (falls back to DELETE FROM, no DDL lock
        // needed). A bare inline "DROP TABLE" here — this method's original form — missed both
        // fallbacks even though DatabaseSchemaHelper already handled them for its own,
        // fixture-wide cleanup path; that gap let Spanner/Firebird failures slip through
        // CompositeKeyTests/MergeConflictTests's own per-class RecreateTableAsync.
        //
        // Retains a bounded retry for OTHER genuinely transient DatabaseException outcomes
        // (IsTransient) as a generic backstop across any dialect, on top of TryDropTableAsync's
        // targeted fallbacks — SqlDialect.ResetConnectionPoolForDdl (SqlContainer.ExecuteNonQueryAsync)
        // already handles the common Firebird DDL-pooling case centrally, so this loop is a safety
        // net, not the primary defense. A flat 200ms x 3 attempts (this method's original form)
        // was confirmed too short under heavy load — the residual, low-frequency Firebird lock
        // conflict (a SerializationConflictException, IsTransient = true) still slipped through
        // with that budget once other test infrastructure fixes eliminated the more common
        // failure modes exposing it. Widened to match ExecuteDdlWithTransientRetryAsync's more
        // generous escalating backoff instead of guessing at a slightly-larger flat delay.
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // requireActualDrop: true — this method's callers immediately issue their own bare
                // CREATE TABLE expecting the name to be free. Firebird's metadata-lock fallback
                // (DELETE FROM, used by DatabaseSchemaHelper's own fixture-wide cleanup) would
                // leave the table behind — emptied but still present — turning that immediately-
                // following CREATE TABLE into a hard "already exists" failure instead of the
                // original transient lock conflict this retry loop exists to absorb.
                await DatabaseSchemaHelper.TryDropTableAsync(context, tableName, requireActualDrop: true)
                    .ConfigureAwait(false);
                return;
            }
            catch (DatabaseException ex) when (ex.IsTransient == true && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt)).ConfigureAwait(false);
            }
        }
    }

    // Verified live: Spanner's CREATE TABLE / CREATE INDEX is a distributed, globally-coordinated
    // schema change that Spanner serializes one at a time per database — under a long integration
    // run where many test classes each issue their own DROP/CREATE cycle back-to-back against the
    // same database, that queue can grow deep enough that a trivially small CREATE TABLE exceeds
    // even the client's command timeout. CommandTimeoutException already defaults IsTransient=true
    // for exactly this reason (see OperationExceptions.cs).
    //
    // Any test class doing its own DROP/CREATE cycle (RecreateTableAsync-style) should run its
    // CREATE statement(s) through this rather than a bare ExecuteNonQueryAsync — a bare call has
    // no protection against the above, and (for any dialect, not just Spanner) no protection
    // against the residual, low-frequency Firebird DDL-vs-connection-pooling lock conflict
    // SqlContainer.ExecuteNonQueryAsync's own pool-reset hook reduces but does not fully
    // eliminate under heavy concurrent load. Originally lived only on CompositeKeyTests; promoted
    // here after MergeConflictTests hit the same Firebird lock conflict with no retry protection
    // at all on its own bare RecreateTableAsync.
    protected static async Task ExecuteDdlWithTransientRetryAsync(IDatabaseContext context, string sql)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var container = context.CreateSqlContainer(sql);
            try
            {
                await container.ExecuteNonQueryAsync().ConfigureAwait(false);
                return;
            }
            // Spanner's own DROP TABLE genuinely does cascade its secondary indices, but the
            // name's removal from the shared schema catalog is not always visible to the very
            // next DDL statement — the very next CREATE INDEX can collide with "Duplicate name in
            // schema: <same index name>" even though the prior DROP TABLE that owned it already
            // returned success. This is NOT a transient condition to retry-and-hope-it-clears —
            // under heavier load the propagation lag was observed to exceed even a 5-attempt,
            // up-to-50s-total backoff. Since the index name and definition are generated
            // deterministically by our own code for a given table, "duplicate name" here always
            // means "the exact index we wanted already exists" — a no-op, not a failure. Treated
            // as success unconditionally rather than retried, which also sidesteps the timing
            // question entirely instead of trying to out-wait a variable-length propagation lag.
            catch (DatabaseException ex) when (context.Product == SupportedDatabase.Spanner &&
                ex.Message.Contains("Duplicate name in schema", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            catch (DatabaseException ex) when (ex.IsTransient == true && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt)).ConfigureAwait(false);
            }
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
