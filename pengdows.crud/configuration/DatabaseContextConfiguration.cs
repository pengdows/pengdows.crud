// =============================================================================
// FILE: DatabaseContextConfiguration.cs
// PURPOSE: POCO configuration for DatabaseContext initialization.
//
// AI SUMMARY:
// - Implements IDatabaseContextConfiguration for full context setup.
// - Core settings:
//   * ConnectionString, ProviderName: Required for connection
//   * ApplicationName: Added to connection string for monitoring
// - Connection mode:
//   * DbMode: Best (auto-detect), Standard, KeepAlive, SingleWriter, SingleConnection
//   * ReadWriteMode: ReadWrite, ReadOnly (WriteOnly converted to ReadWrite)
// - Statement preparation:
//   * ForceManualPrepare, DisablePrepare: Override provider defaults
// - Pool governance:
//   * WritePoolSize, ReadPoolSize: Custom pool limits
//   * PoolAcquireTimeout: How long to wait for pool permit
//   * EnablePoolGovernor: Enable/disable permit-based pooling
// - Mode locking:
//   * ModeLockTimeout: Timeout for SingleWriter/SingleConnection lock
// - Metrics:
//   * EnableMetrics, MetricsOptions: Performance tracking configuration
// =============================================================================

using System;
using pengdows.crud.enums;
using pengdows.crud.metrics;

namespace pengdows.crud.configuration;

/// <summary>
/// Configuration options for DatabaseContext behavior.
/// </summary>
public class DatabaseContextConfiguration : IDatabaseContextConfiguration
{
    internal const int DefaultPoolAcquireSeconds = 5;
    internal const int DefaultModeLockSeconds = 30;

    /// <summary>
    /// Database connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Read-only connection string. When empty, the write connection string is used as the base.
    /// </summary>
    public string ReadOnlyConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database provider name (e.g., "System.Data.SqlClient", "Npgsql").
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Connection lifecycle mode. Defaults to <see cref="enums.DbMode.Best"/> for automatic selection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recommended:</b> Leave this as <see cref="enums.DbMode.Best"/> (default) to automatically
    /// select the optimal mode based on database type and connection string.
    /// </para>
    /// <para>
    /// <b>Explicit Mode Selection:</b> Set to a specific mode if you need to override auto-detection.
    /// For example:
    /// <list type="bullet">
    ///   <item><description><see cref="enums.DbMode.Standard"/> - Force connection-per-operation (client-server DBs)</description></item>
    ///   <item><description><see cref="enums.DbMode.KeepAlive"/> - Standard Mode with an unused connection to keep the database from unloading.</description></item>
    ///   <item><description><see cref="enums.DbMode.SingleWriter"/> - Force single writer mode (file-based DBs)</description></item>
    ///   <item><description><see cref="enums.DbMode.SingleConnection"/> - Force single connection (testing, :memory:)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Mode/Database Mismatch Warnings:</b> If you explicitly select a mode that doesn't match
    /// your database characteristics, pengdows.crud will log performance warnings. These warnings
    /// indicate suboptimal configuration, not correctness issues.
    /// </para>
    /// <para>
    /// See <see cref="enums.DbMode"/> for detailed documentation on each mode's behavior,
    /// use cases, and operational characteristics.
    /// </para>
    /// </remarks>
    public DbMode DbMode { get; set; } = DbMode.Best;

    private ReadWriteMode _readWriteMode = ReadWriteMode.ReadWrite;

    public ReadWriteMode ReadWriteMode
    {
        get => _readWriteMode;
        set => _readWriteMode = value == ReadWriteMode.WriteOnly ? ReadWriteMode.ReadWrite : value;
    }

    public bool? ForceManualPrepare { get; set; }
    public bool? DisablePrepare { get; set; }
    public bool EnableMetrics { get; set; } = false;
    public MetricsOptions MetricsOptions { get; set; } = MetricsOptions.Default;

    private int? _maxConcurrentWrites;
    private int? _maxConcurrentReads;

    /// <summary>
    /// Maximum number of concurrent write operations allowed by the governor.
    /// Overrides <see cref="ReadPoolSize"/> for backward compatibility.
    /// </summary>
    public int? MaxConcurrentWrites
    {
        get => _maxConcurrentWrites;
        set => _maxConcurrentWrites = value;
    }

    /// <summary>
    /// Maximum number of concurrent read operations allowed by the governor.
    /// Overrides <see cref="ReadPoolSize"/> for backward compatibility.
    /// </summary>
    public int? MaxConcurrentReads
    {
        get => _maxConcurrentReads;
        set => _maxConcurrentReads = value;
    }

    /// <summary>
    /// Legacy alias for <see cref="MaxConcurrentWrites"/>.
    /// </summary>
    [Obsolete("Use MaxConcurrentWrites instead.")]
    public int? WritePoolSize
    {
        get => MaxConcurrentWrites;
        set => MaxConcurrentWrites = value;
    }

    /// <summary>
    /// Legacy alias for <see cref="MaxConcurrentReads"/>.
    /// </summary>
    [Obsolete("Use MaxConcurrentReads instead.")]
    public int? ReadPoolSize
    {
        get => MaxConcurrentReads;
        set => MaxConcurrentReads = value;
    }

    /// <summary>
    /// When true, the governor enforces writer-preference gates (turnstile) for SingleWriter mode.
    /// </summary>
    public bool EnableWriterPreference { get; set; } = true;
    public TimeSpan PoolAcquireTimeout { get; set; } = TimeSpan.FromSeconds(DefaultPoolAcquireSeconds);
    public TimeSpan? ModeLockTimeout { get; set; } = TimeSpan.FromSeconds(DefaultModeLockSeconds);
    public bool EnablePoolGovernor { get; set; } = true;
    
    public string ApplicationName { get; set; } = string.Empty;
}
