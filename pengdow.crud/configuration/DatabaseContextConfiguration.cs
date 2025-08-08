#region

using pengdow.crud.enums;

#endregion

namespace pengdow.crud.configuration;

public class DatabaseContextConfiguration : IDatabaseContextConfiguration
{
    public string ConnectionString { get; set; }
    public string ProviderName { get; set; }
    public DbMode DbMode { get; set; } = DbMode.Standard;
    public ReadWriteMode ReadWriteMode { get; set; } = ReadWriteMode.ReadWrite;
}