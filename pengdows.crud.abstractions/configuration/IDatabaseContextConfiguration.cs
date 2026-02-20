#region

using System;
using pengdows.crud.enums;
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
    /// Override to force manual prepare on or off for all commands.
    /// When set, this overrides the dialect's PrepareStatements setting.
    /// </summary>
    bool? ForceManualPrepare { get; set; }

    /// <summary>
    /// When true, disables prepare for all commands regardless of dialect settings.
    /// Takes precedence over ForceManualPrepare.
    /// </summary>
    bool? DisablePrepare { get; set; }

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
    /// </remarks>
    bool EnableSingleWriterFairness { get; set; }
}
