using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.configuration;

public class DatabaseContextConfigurationTests
{
    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        var config = new DatabaseContextConfiguration();
        
        Assert.Equal(string.Empty, config.ConnectionString);
        Assert.Equal(string.Empty, config.ProviderName);
        Assert.Equal(DbMode.Standard, config.DbMode);
        Assert.Equal(ReadWriteMode.ReadWrite, config.ReadWriteMode);
        Assert.False(config.SetDefaultSearchPath);
    }

    [Fact]
    public void ConnectionString_CanBeSetAndRetrieved()
    {
        var config = new DatabaseContextConfiguration();
        const string connectionString = "Server=localhost;Database=test;";
        
        config.ConnectionString = connectionString;
        
        Assert.Equal(connectionString, config.ConnectionString);
    }

    [Fact]
    public void ProviderName_CanBeSetAndRetrieved()
    {
        var config = new DatabaseContextConfiguration();
        const string providerName = "System.Data.SqlClient";
        
        config.ProviderName = providerName;
        
        Assert.Equal(providerName, config.ProviderName);
    }

    [Fact]
    public void DbMode_CanBeSetAndRetrieved()
    {
        var config = new DatabaseContextConfiguration();
        
        config.DbMode = DbMode.KeepAlive;
        
        Assert.Equal(DbMode.KeepAlive, config.DbMode);
    }

    [Fact]
    public void ReadWriteMode_CanBeSetAndRetrieved()
    {
        var config = new DatabaseContextConfiguration();
        
        config.ReadWriteMode = ReadWriteMode.ReadOnly;
        
        Assert.Equal(ReadWriteMode.ReadOnly, config.ReadWriteMode);
    }

    [Fact]
    public void SetDefaultSearchPath_CanBeSetAndRetrieved()
    {
        var config = new DatabaseContextConfiguration();
        
        config.SetDefaultSearchPath = true;
        
        Assert.True(config.SetDefaultSearchPath);
    }

    [Fact]
    public void AllProperties_CanBeSetTogether()
    {
        var config = new DatabaseContextConfiguration();
        const string connectionString = "Server=localhost;Database=test;";
        const string providerName = "System.Data.SqlClient";
        
        config.ConnectionString = connectionString;
        config.ProviderName = providerName;
        config.DbMode = DbMode.KeepAlive;
        config.ReadWriteMode = ReadWriteMode.WriteOnly;
        config.SetDefaultSearchPath = true;
        
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.Equal(providerName, config.ProviderName);
        Assert.Equal(DbMode.KeepAlive, config.DbMode);
        Assert.Equal(ReadWriteMode.WriteOnly, config.ReadWriteMode);
        Assert.True(config.SetDefaultSearchPath);
    }

    [Fact]
    public void ImplementsIDatabaseContextConfiguration()
    {
        var config = new DatabaseContextConfiguration();
        
        Assert.IsAssignableFrom<IDatabaseContextConfiguration>(config);
    }
}