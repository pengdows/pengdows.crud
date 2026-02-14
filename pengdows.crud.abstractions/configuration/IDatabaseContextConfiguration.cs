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
    MetricsOptions MetricsOptions { get; set; }

    /// <summary>
    /// Governor-driven limit for concurrent write operations.
    /// </summary>
    int? MaxConcurrentWrites { get; set; }

    /// <summary>
    /// Governor-driven limit for concurrent read operations.
    /// </summary>
    int? MaxConcurrentReads { get; set; }

    /// <summary>
    /// Timeout for internal pool permit acquisition. Should be lower than provider connection timeout.
    /// </summary>
    TimeSpan PoolAcquireTimeout { get; set; }

    /// <summary>
    /// Optional timeout for shared-connection mode locks. Null disables timeouts (wait forever).
    /// </summary>
    TimeSpan? ModeLockTimeout { get; set; }

    /// <summary>
    /// Enables or disables internal pool governor behavior.
    /// </summary>
    bool EnablePoolGovernor { get; set; }

    /// <summary>
    /// Optional value passed to the provider (Application Name / Client Info) used for telemetry/connection tagging.
    /// </summary>
    string ApplicationName { get; set; }

    /// <summary>
    /// When true, enables the writer-preference turnstile during SingleWriter mode.
    /// </summary>
    bool EnableWriterPreference { get; set; }
}
