#region

using pengdows.crud.enums;

#endregion

namespace pengdows.crud.configuration;

public class DatabaseContextConfiguration : IDatabaseContextConfiguration
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DbMode DbMode { get; set; } = DbMode.Standard;
    public ReadWriteMode ReadWriteMode { get; set; } = ReadWriteMode.ReadWrite;
    public bool SetDefaultSearchPath { get; set; }
}
