#region

using System;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;

#endregion

namespace pengdows.crud.configuration;

/// <summary>
/// Defines configuration options for establishing and managing a database context.
/// </summary>
public interface IDatabaseContextConfiguration
{
    /// <summary>
    /// Gets or sets the connection string used to connect to the database.
    /// </summary>
    string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the connection string used for read-only operations.
    /// When empty, the write connection string is used as the base.
    /// </summary>
    string ReadOnlyConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the ADO.NET provider invariant name.
    /// </summary>
    /// <remarks>
    /// <b>When this configuration is resolved through <c>ITenantContextRegistry</c>/<c>DbProviderLoader</c>-based
    /// dependency injection:</b> this value is looked up as a keyed <c>DbProviderFactory</c> DI
    /// registration, and <c>DbProviderLoader</c> registers each factory under its
    /// <c>DatabaseProviders</c> configuration section <em>key</em> — not the
    /// <c>DatabaseProviderConfig.ProviderName</c> field configured inside that section. In that
    /// path, this property must equal the <c>DatabaseProviders</c> section key
    /// (e.g. <c>"DatabaseProviders:MyProviderKey": { ... }</c> requires
    /// <c>ProviderName = "MyProviderKey"</c>), not the underlying ADO.NET invariant name, even
    /// though that name is what the section's own <c>ProviderName</c> field typically holds.
    /// </remarks>
    string ProviderName { get; set; }

    /// <summary>
    /// Gets or sets the database engine mode to target.
    /// </summary>
    DbMode DbMode { get; set; }

    /// <summary>
    /// Gets or sets whether the context is in read-only, write-only, or read-write mode.
    /// </summary>
    ReadWriteMode ReadWriteMode { get; set; }

    /// <summary>
    /// Specifies how database commands should handle statement preparation.
    /// </summary>
    CommandPrepareMode PrepareMode { get; set; }

    /// <summary>
    /// Maximum number of reader plan cache entries to maintain per TableGateway instance.
    /// </summary>
    /// <remarks>
    /// <b>Read once, at gateway construction, from whichever <see cref="IDatabaseContext"/> is
    /// passed to the gateway's constructor — not re-read per operation.</b> In the documented
    /// context-per-tenant multi-tenancy pattern, one singleton gateway is reused across many
    /// different tenant <see cref="IDatabaseContext"/> instances passed as an optional override
    /// parameter to individual calls (e.g. <c>RetrieveOneAsync(id, tenantContext)</c>); that
    /// per-call context changes only which physical connection executes the operation, never
    /// this cache-size setting or the gateway's cached entity metadata. This is intentional:
    /// compiled reader plans are keyed by the query result's column shape (an entity-level,
    /// tenant-independent property, not a function of which tenant's connection produced the
    /// reader), so one shared cache per gateway is correct and avoids pointless recompilation
    /// across tenants using the same entity. Treat this value as a gateway-lifetime capacity
    /// tuning knob; if genuinely different tenants need genuinely different cache sizes,
    /// construct separate gateway instances rather than relying on a shared singleton gateway
    /// to pick up a later tenant's value.
    /// </remarks>
    int? ReaderPlanCacheSize { get; set; }

    /// <summary>
    /// Gets or sets whether metrics collection is enabled for this context.
    /// When false (default), no metrics collection overhead is incurred.
    /// </summary>
    bool EnableMetrics { get; set; }

    /// <summary>
    /// Metrics collection options for the associated <see cref="IDatabaseContext"/>.
    /// </summary>
    IMetricsOptions MetricsOptions { get; set; }

    /// <summary>
    /// Maximum number of concurrent write operations admitted by the connection governor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is NOT the ADO.NET <c>Max Pool Size</c>.</b> It controls how many concurrent
    /// write operations the library's admission governor allows at once. ADO.NET pooling is
    /// configured separately via the connection string (e.g., <c>Max Pool Size=N</c>).
    /// </para>
    /// <para>
    /// When <c>null</c> (the default), the governor defaults to the ADO.NET <c>Max Pool Size</c>
    /// parsed from the connection string using the dialect's pool-size key, or the dialect's
    /// built-in default if that key is absent.
    /// </para>
    /// <para>
    /// Setting this lower than the ADO.NET pool size limits library-level concurrency.
    /// Setting it higher has no additional effect — ADO.NET becomes the bottleneck.
    /// For predictable behavior, align this value with your ADO.NET <c>Max Pool Size</c>.
    /// </para>
    /// </remarks>
    int? MaxConcurrentWrites { get; set; }

    /// <summary>
    /// Maximum number of concurrent read operations admitted by the connection governor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is NOT the ADO.NET <c>Max Pool Size</c>.</b> It controls how many concurrent
    /// read operations the library's admission governor allows at once. ADO.NET pooling is
    /// configured separately via the connection string (e.g., <c>Max Pool Size=N</c>).
    /// </para>
    /// <para>
    /// When <c>null</c> (the default), the governor defaults to the ADO.NET <c>Max Pool Size</c>
    /// parsed from the connection string using the dialect's pool-size key, or the dialect's
    /// built-in default if that key is absent.
    /// </para>
    /// <para>
    /// Setting this lower than the ADO.NET pool size limits library-level concurrency.
    /// Setting it higher has no additional effect — ADO.NET becomes the bottleneck.
    /// For predictable behavior, align this value with your ADO.NET <c>Max Pool Size</c>.
    /// </para>
    /// </remarks>
    int? MaxConcurrentReads { get; set; }

    /// <summary>
    /// How long to wait for a governor permit before throwing <c>PoolSaturatedException</c>.
    /// </summary>
    /// <remarks>
    /// Should be set lower than the ADO.NET connection timeout so the library surfaces a
    /// meaningful error before the driver does.
    /// </remarks>
    TimeSpan PoolAcquireTimeout { get; set; }

    /// <summary>
    /// Timeout for internal mode locks used in <see cref="enums.DbMode.SingleWriter"/> and
    /// <see cref="enums.DbMode.SingleConnection"/> modes.
    /// <c>null</c> means wait indefinitely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This timeout governs a different bottleneck than <see cref="PoolAcquireTimeout"/>:
    /// <list type="bullet">
    ///   <item><see cref="PoolAcquireTimeout"/> — waiting for a governor permit (pool admission, default 5 s)</item>
    ///   <item><see cref="ModeLockTimeout"/> — waiting for a shared-connection write lock (default 30 s)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Mode locks guard long-running transactions, which is why the default (30 s) is higher than
    /// the governor timeout (5 s). <c>null</c> means wait indefinitely — appropriate when you prefer
    /// to block rather than abort long transactions. Set both timeouts explicitly if you want
    /// consistent failure behavior across all wait surfaces.
    /// </para>
    /// </remarks>
    TimeSpan? ModeLockTimeout { get; set; }

    /// <summary>
    /// Optional value passed to the provider (Application Name / Client Info) used for telemetry/connection tagging.
    /// </summary>
    string ApplicationName { get; set; }

    /// <summary>
    /// When true, enables the writer-preference turnstile in <see cref="enums.DbMode.SingleWriter"/> mode.
    /// </summary>
    /// <remarks>
    /// <b>This setting has no effect in any mode other than <see cref="enums.DbMode.SingleWriter"/>.</b>
    /// <para>
    /// In SingleWriter mode the governor limits concurrent writes to one. When contention occurs
    /// (multiple callers racing for the single write slot), the turnstile gives the waiting writer
    /// priority over incoming readers by blocking new read attempts until the write slot is acquired.
    /// This reduces — but does not eliminate — writer starvation under sustained read pressure.
    /// </para>
    /// <para>
    /// <b>Known limitation:</b> readers that are already queued on the semaphore at the moment a
    /// writer acquires the turnstile are not displaced — they will complete before the writer runs.
    /// Only readers that arrive <em>after</em> the writer has grabbed the turnstile are held back.
    /// Under a sustained burst of reads, a writer may still experience transient delays before the
    /// pre-queued readers drain. Monitor <c>PoolStatisticsSnapshot.TotalTurnstileTimeouts</c> to
    /// detect contention and consider increasing <see cref="PoolAcquireTimeout"/> or reducing
    /// <see cref="MaxConcurrentReads"/> if starvation persists.
    /// </para>
    /// </remarks>
    bool EnableSingleWriterFairness { get; set; }

    /// <summary>
    /// Controls how a connection is handled when applying session settings fails on first open.
    /// Defaults to <see cref="SessionInitializationFailureMode.BestEffort"/> — logs and proceeds
    /// with the connection in an unknown session state (current 2.0 behavior).
    /// </summary>
    /// <remarks>
    /// Does not affect the separate, transaction-level read-only enforcement mechanism used by
    /// MySQL, MariaDB, and Oracle, which remains best-effort regardless of this setting.
    /// </remarks>
    SessionInitializationFailureMode SessionInitializationFailureMode { get; set; }

    /// <summary>
    /// Maximum number of callers allowed to queue for a write-governor slot before further
    /// callers are rejected immediately with <c>PoolSaturatedException</c>, rather than waiting
    /// out the full <see cref="PoolAcquireTimeout"/>. <c>null</c> (default) uses the governor's
    /// built-in default (proportional to <see cref="MaxConcurrentWrites"/>).
    /// </summary>
    int? MaxQueuedWrites { get; set; }

    /// <summary>
    /// Maximum number of callers allowed to queue for a read-governor slot before further
    /// callers are rejected immediately with <c>PoolSaturatedException</c>, rather than waiting
    /// out the full <see cref="PoolAcquireTimeout"/>. <c>null</c> (default) uses the governor's
    /// built-in default (proportional to <see cref="MaxConcurrentReads"/>).
    /// </summary>
    int? MaxQueuedReads { get; set; }

    /// <summary>
    /// When <c>true</c>, throws <see cref="InvalidOperationException"/> at construction if another
    /// live <c>DatabaseContext</c> in this process already uses the same connection string (or
    /// <see cref="ReadOnlyConnectionString"/>). Defaults to <c>false</c> — current (2.0) behavior,
    /// with no cross-context checking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two <c>DatabaseContext</c> instances pointed at the same physical connection string each run
    /// independent <c>PoolGovernor</c> admission control — neither one knows about the other, so
    /// their combined admitted connections can exceed what the underlying provider pool was sized
    /// for. The supported pattern is one <c>DatabaseContext</c> per connection string, registered
    /// as a singleton; this flag is an opt-in safety net for production that catches the common
    /// misconfiguration (e.g. accidentally registering the context as non-singleton) without
    /// affecting anyone who doesn't enable it.
    /// </para>
    /// <para>
    /// Left <c>false</c> by default specifically so test suites (including this library's own,
    /// and consumers using <c>pengdows.crud.fakeDb</c>) can freely construct many contexts against
    /// the same connection string — a fake/test connection string doesn't represent a real
    /// contended physical resource, so the check would produce false positives there.
    /// </para>
    /// </remarks>
    bool EnforceUniqueConnectionString { get; set; }
}