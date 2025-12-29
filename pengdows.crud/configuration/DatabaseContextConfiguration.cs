#region

using pengdows.crud.enums;
using pengdows.crud.metrics;

#endregion

namespace pengdows.crud.configuration;

public class DatabaseContextConfiguration : IDatabaseContextConfiguration
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
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
}
