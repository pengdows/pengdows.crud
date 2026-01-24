#region

using pengdows.crud.enums;
using pengdows.crud.metrics;

#endregion

namespace pengdows.crud.configuration;

/// <summary>
/// Configuration options for DatabaseContext behavior.
/// </summary>
public class DatabaseContextConfiguration : IDatabaseContextConfiguration
{
    /// <summary>
    /// Database connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

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

    public int? WritePoolSize { get; set; }
    public int? ReadPoolSize { get; set; }
    public TimeSpan PoolAcquireTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan? ModeLockTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnablePoolGovernor { get; set; } = true;
}
