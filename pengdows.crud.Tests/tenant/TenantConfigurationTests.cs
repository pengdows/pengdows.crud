using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests.tenant;

public class TenantConfigurationTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        var config = new TenantConfiguration();
        
        Assert.Equal(string.Empty, config.Name);
        Assert.NotNull(config.DatabaseContextConfiguration);
        Assert.IsType<DatabaseContextConfiguration>(config.DatabaseContextConfiguration);
    }

    [Fact]
    public void Name_CanBeSetAndRetrieved()
    {
        var config = new TenantConfiguration();
        const string name = "TestTenant";
        
        config.Name = name;
        
        Assert.Equal(name, config.Name);
    }

    [Fact]
    public void DatabaseContextConfiguration_CanBeSetAndRetrieved()
    {
        var config = new TenantConfiguration();
        var dbConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=localhost;Database=test;",
            ProviderName = "System.Data.SqlClient",
            DbMode = DbMode.KeepAlive
        };
        
        config.DatabaseContextConfiguration = dbConfig;
        
        Assert.Same(dbConfig, config.DatabaseContextConfiguration);
        Assert.Equal("Server=localhost;Database=test;", config.DatabaseContextConfiguration.ConnectionString);
        Assert.Equal("System.Data.SqlClient", config.DatabaseContextConfiguration.ProviderName);
        Assert.Equal(DbMode.KeepAlive, config.DatabaseContextConfiguration.DbMode);
    }

    [Fact]
    public void DatabaseContextConfiguration_DefaultIsNotNull()
    {
        var config = new TenantConfiguration();
        
        Assert.NotNull(config.DatabaseContextConfiguration);
        Assert.Equal(string.Empty, config.DatabaseContextConfiguration.ConnectionString);
        Assert.Equal(string.Empty, config.DatabaseContextConfiguration.ProviderName);
        Assert.Equal(DbMode.Best, config.DatabaseContextConfiguration.DbMode);
    }

    [Fact]
    public void AllProperties_CanBeSetTogether()
    {
        var config = new TenantConfiguration();
        const string name = "MultiTenant";
        var dbConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=remote;Database=multitenant;",
            ProviderName = "Npgsql",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        
        config.Name = name;
        config.DatabaseContextConfiguration = dbConfig;
        
        Assert.Equal(name, config.Name);
        Assert.Same(dbConfig, config.DatabaseContextConfiguration);
        Assert.Equal("Server=remote;Database=multitenant;", config.DatabaseContextConfiguration.ConnectionString);
        Assert.Equal("Npgsql", config.DatabaseContextConfiguration.ProviderName);
        Assert.Equal(DbMode.KeepAlive, config.DatabaseContextConfiguration.DbMode);
        Assert.Equal(ReadWriteMode.ReadOnly, config.DatabaseContextConfiguration.ReadWriteMode);
    }

    [Fact]
    public void Name_HandlesNullValue()
    {
        var config = new TenantConfiguration();
        
        config.Name = null!;
        
        Assert.Null(config.Name);
    }

    [Fact]
    public void DatabaseContextConfiguration_CanBeSetToNull()
    {
        var config = new TenantConfiguration();
        
        config.DatabaseContextConfiguration = null!;
        
        Assert.Null(config.DatabaseContextConfiguration);
    }
}