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
        var failureReasons = new List<string>();

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
                    $"[{DateTime.UtcNow:HH:mm:ss.fff}] ❌ {provider} failed to initialize: {ex.Message}");
                failureReasons.Add($"{provider}: {ex.Message}");
            }
        }

        EnsureProvidersInitialized(GetType().Name, requestedProviders, contexts.Count, skipReasons, failureReasons);

        DatabaseContexts = contexts;

        foreach (var (provider, context) in DatabaseContexts)
        {
            var setupStart = DateTime.UtcNow;
            Output.WriteLine($"[{setupStart:HH:mm:ss.fff}] Resetting {provider} database...");
            IntegrationTraceLog.Write(provider, "cleanup start", Output);
            await RunWithTimeoutAsync(() => CleanupDatabaseAsync(provider, context), SetupTimeout, provider,
                "cleanup");
            IntegrationTraceLog.Write(provider,
                $"cleanup done elapsedMs={(DateTime.UtcNow - setupStart).TotalMilliseconds:F0}", Output);
            Output.WriteLine(
                $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} cleanup complete, running SetupDatabaseAsync...");
            IntegrationTraceLog.Write(provider, "setup start", Output);
            await RunWithTimeoutAsync(() => SetupDatabaseAsync(provider, context), SetupTimeout, provider, "setup");
            IntegrationTraceLog.Write(provider,
                $"setup done elapsedMs={(DateTime.UtcNow - setupStart).TotalMilliseconds:F0}", Output);
            Output.WriteLine(
                $"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} setup completed (took {(DateTime.UtcNow - setupStart).TotalMilliseconds:F0}ms)");
        }

        Output.WriteLine(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] ✅ All initialization complete (total: {(DateTime.UtcNow - totalStart).TotalMilliseconds:F0}ms)");
    }

    /// <summary>
    /// Decides the outcome once every requested provider has been tried. A provider that is enabled
    /// for this run but failed to initialize fails the test: turning it into a skip (or silently
    /// testing only the providers that did start) would hide a broken database. Only when every
    /// requested provider was excluded by configuration (INTEGRATION_ONLY, an opt-in provider that
    /// is not enabled) is the test skipped.
    /// </summary>
    internal static void EnsureProvidersInitialized(string testClass, IReadOnlyList<SupportedDatabase> requested,
        int initializedCount, IReadOnlyList<string> exclusionReasons, IReadOnlyList<string> failureReasons)
    {
        if (failureReasons.Count > 0)
        {
            throw new InvalidOperationException(
                $"{testClass}: {failureReasons.Count} enabled provider(s) failed to initialize: " +
                $"{string.Join("; ", failureReasons)}.");
        }

        if (initializedCount == 0)
        {
            var requestedText = requested.Count == 0
                ? "none (check INTEGRATION_ONLY env var or GetSupportedProviders override)"
                : string.Join(", ", requested);
            var reasonDetail = exclusionReasons.Count > 0
                ? $" Excluded by configuration: {string.Join("; ", exclusionReasons)}."
                : string.Empty;
            throw new Xunit.SkipException(
                $"{testClass} requires [{requestedText}] but none is enabled for this run.{reasonDetail}");
        }
    }

    /// <summary>
    /// HARN-006: a test that targets one provider skips when configuration (INTEGRATION_ONLY, an
    /// opt-in provider that is not enabled) excluded it, and fails when the provider is enabled
    /// but unavailable - the same rule <see cref="EnsureProvidersInitialized"/> applies per class.
    /// </summary>
    /// <summary>
    /// The databases a test that declares its own list should run: every listed database that is
    /// available. Skips only when configuration excluded all of them; fails when a listed database
    /// is enabled but unavailable.
    /// </summary>
    internal static IReadOnlyList<SupportedDatabase> SelectTargetedProviders(
        IReadOnlyCollection<SupportedDatabase> providers,
        IReadOnlyCollection<SupportedDatabase> enabledProviders,
        IReadOnlyCollection<SupportedDatabase> availableProviders)
    {
        var missing = providers.Where(p => enabledProviders.Contains(p) && !availableProviders.Contains(p)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Provider(s) {string.Join(", ", missing)} enabled but not available for testing.");
        }

        var selected = providers.Where(availableProviders.Contains).ToArray();
        if (selected.Length == 0)
        {
            throw new Xunit.SkipException(
                $"None of {string.Join(", ", providers)} is enabled for this run (INTEGRATION_ONLY or an opt-in provider that is not enabled).");
        }

        return selected;
    }

    internal static void EnsureTargetedProviderAvailable(SupportedDatabase provider,
        IReadOnlyList<SupportedDatabase> enabledProviders, bool available)
    {
        if (available)
        {
            return;
        }

        if (!enabledProviders.Contains(provider))
        {
            throw new Xunit.SkipException(
                $"{provider} is excluded by configuration for this run (INTEGRATION_ONLY or an opt-in provider that is not enabled).");
        }

        throw new InvalidOperationException($"Provider {provider} is enabled but not available for testing.");
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

    // HARN-007: a statement that never returns must fail its provider, not hang the whole run. Seen
    // with Spanner Omni DDL: the server applied it, but the client waited 20+ minutes in
    // NpgsqlDataReader.NextResult although the connection string sets CommandTimeout=60.
    internal static readonly TimeSpan SetupTimeout =
        ParseTimeout(Environment.GetEnvironmentVariable("INTEGRATION_SETUP_TIMEOUT_SECONDS"), TimeSpan.FromMinutes(5));

    internal static readonly TimeSpan TestTimeout =
        ParseTimeout(Environment.GetEnvironmentVariable("INTEGRATION_TEST_TIMEOUT_SECONDS"), TimeSpan.FromMinutes(10));

    internal static TimeSpan ParseTimeout(string? seconds, TimeSpan fallback) =>
        int.TryParse(seconds, out var value) && value > 0 ? TimeSpan.FromSeconds(value) : fallback;

    internal static async Task RunWithTimeoutAsync(Func<Task> work, TimeSpan timeout, SupportedDatabase provider,
        string phase)
    {
        var task = work();
        try
        {
            await task.WaitAsync(timeout);
        }
        catch (TimeoutException) when (!task.IsCompleted)
        {
            throw new TimeoutException(
                $"{provider} {phase} did not finish within {timeout.TotalSeconds:F0}s: a statement stalled " +
                "(HARN-007, seen with Spanner Omni DDL). Raise INTEGRATION_SETUP_TIMEOUT_SECONDS / " +
                "INTEGRATION_TEST_TIMEOUT_SECONDS if the work is legitimately slower.");
        }
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
                await RunWithTimeoutAsync(() => testAction(provider, context), TestTimeout, provider, "test");
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
    /// Run a test against only the listed databases, for a feature only they have (for example a
    /// stored procedure in one engine's syntax). See <see cref="SelectTargetedProviders"/>.
    /// </summary>
    protected async Task RunTestAgainstProvidersAsync(
        IReadOnlyCollection<SupportedDatabase> providers,
        Func<SupportedDatabase, IDatabaseContext, Task> testAction,
        [CallerMemberName] string? testName = null)
    {
        var selected = SelectTargetedProviders(providers, IntegrationTestConfiguration.EnabledProviders,
            DatabaseContexts.Keys.ToArray());
        var failures = new List<(SupportedDatabase Provider, Exception Error)>();
        foreach (var provider in selected)
        {
            try
            {
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Running {testName} against {provider}...");
                await RunWithTimeoutAsync(() => testAction(provider, DatabaseContexts[provider]), TestTimeout,
                    provider, "test");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {provider} test failed: {ex.Message}");
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
    protected async Task RunTestAgainstProviderAsync(
        SupportedDatabase provider,
        Func<IDatabaseContext, Task> testAction,
        [CallerMemberName] string? testName = null)
    {
        var available = DatabaseContexts.TryGetValue(provider, out var context);
        EnsureTargetedProviderAvailable(provider, IntegrationTestConfiguration.EnabledProviders, available);

        var start = DateTime.UtcNow;
        IntegrationTraceLog.Write(provider, $"test start name={testName ?? "<unknown>"}", Output);
        await testAction(context!);
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
            // Only for CREATE INDEX: a duplicate TABLE name means the DROP didn't take, and
            // accepting it would run the test against a stale table of unknown shape (REV-067).
            catch (DatabaseException ex) when (context.Product == SupportedDatabase.Spanner &&
                IsCreateIndex(sql) &&
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

    private static bool IsCreateIndex(string sql)
    {
        var words = sql.TrimStart().Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 &&
               words[0].Equals("CREATE", StringComparison.OrdinalIgnoreCase) &&
               (words[1].Equals("INDEX", StringComparison.OrdinalIgnoreCase) ||
                (words.Length >= 3 &&
                 (words[1].Equals("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                  words[1].Equals("NULL_FILTERED", StringComparison.OrdinalIgnoreCase)) &&
                 words[2].Equals("INDEX", StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>
    /// Capability: whether an upsert that a [Version] guard skips is reported through rows
    /// affected, so the gateways can throw ConcurrencyConflictException. Mirrors the gateways' own
    /// rule: ON CONFLICT ... DO UPDATE ... WHERE, or a MERGE whose rows affected reveals the skipped
    /// row (<c>MergeUpsertReportsSkippedVersionRow</c>; false for Firebird and Sybase ASE). MySQL-family
    /// ON DUPLICATE KEY has neither.
    /// </summary>
    protected static bool UpsertRefusesVersionedEntities(IDatabaseContext context)
    {
        // Capability: a MERGE with no conditional matched clause in any form (Informix) cannot carry
        // the [Version] check, so the gateways refuse a versioned upsert with NotSupportedException
        // (SupportsMergeMatchedCondition = false) instead of silently overwriting.
        var dialect = context.GetDialect();
        return !dialect.SupportsOnConflictWhere && !dialect.SupportsOnDuplicateKey && dialect.SupportsMerge
               && !pengdows.crud.dialects.InternalSqlDialectExtensions.SupportsMergeMatchedCondition(dialect);
    }

    protected static bool UpsertDetectsStaleVersion(IDatabaseContext context)
    {
        var dialect = context.GetDialect();
        return dialect.SupportsOnConflictWhere
               || (dialect.SupportsMerge && pengdows.crud.dialects.InternalSqlDialectExtensions
                   .MergeUpsertReportsSkippedVersionRow(dialect));
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
               || message.Contains("ora-00942")
               || message.Contains("sql0204n")
               || message.Contains("is an undefined name")
               || message.Contains("is not in the database")
               // SAP HANA (error 259): "invalid table name: <name>"
               || message.Contains("invalid table name");
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

        if (provider == SupportedDatabase.SapHana && !IntegrationTestConfiguration.ShouldIncludeSapHana)
        {
            return "requires INCLUDE_SAPHANA=true to enable SAP HANA tests";
        }

        if (provider == SupportedDatabase.InterBase && !IntegrationTestConfiguration.ShouldIncludeInterBase)
        {
            return "requires INCLUDE_INTERBASE=true to enable InterBase tests";
        }

        if (provider == SupportedDatabase.Access && !IntegrationTestConfiguration.ShouldIncludeAccess)
        {
            return "Access tests run only on Windows";
        }

        return "provider is not in the enabled list for this test run";
    }

}
