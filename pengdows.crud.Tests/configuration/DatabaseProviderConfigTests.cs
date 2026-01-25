using pengdows.crud.configuration;
using Xunit;

namespace pengdows.crud.Tests.configuration;

public class DatabaseProviderConfigTests
{
    [Fact]
    public void Constructor_InitializesWithEmptyStrings()
    {
        var config = new DatabaseProviderConfig();

        Assert.Equal("", config.ProviderName);
        Assert.Equal("", config.FactoryType);
        Assert.Equal("", config.AssemblyPath);
        Assert.Equal("", config.AssemblyName);
    }

    [Fact]
    public void ProviderName_CanBeSetAndRetrieved()
    {
        var config = new DatabaseProviderConfig();
        const string providerName = "System.Data.SqlClient";

        config.ProviderName = providerName;

        Assert.Equal(providerName, config.ProviderName);
    }

    [Fact]
    public void FactoryType_CanBeSetAndRetrieved()
    {
        var config = new DatabaseProviderConfig();
        const string factoryType = "System.Data.SqlClient.SqlClientFactory";

        config.FactoryType = factoryType;

        Assert.Equal(factoryType, config.FactoryType);
    }

    [Fact]
    public void AssemblyPath_CanBeSetAndRetrieved()
    {
        var config = new DatabaseProviderConfig();
        const string assemblyPath = "/path/to/assembly.dll";

        config.AssemblyPath = assemblyPath;

        Assert.Equal(assemblyPath, config.AssemblyPath);
    }

    [Fact]
    public void AssemblyName_CanBeSetAndRetrieved()
    {
        var config = new DatabaseProviderConfig();
        const string assemblyName = "System.Data.SqlClient";

        config.AssemblyName = assemblyName;

        Assert.Equal(assemblyName, config.AssemblyName);
    }

    [Fact]
    public void AllProperties_CanBeSetTogether()
    {
        var config = new DatabaseProviderConfig();
        const string providerName = "System.Data.SqlClient";
        const string factoryType = "System.Data.SqlClient.SqlClientFactory";
        const string assemblyPath = "/path/to/assembly.dll";
        const string assemblyName = "System.Data.SqlClient";

        config.ProviderName = providerName;
        config.FactoryType = factoryType;
        config.AssemblyPath = assemblyPath;
        config.AssemblyName = assemblyName;

        Assert.Equal(providerName, config.ProviderName);
        Assert.Equal(factoryType, config.FactoryType);
        Assert.Equal(assemblyPath, config.AssemblyPath);
        Assert.Equal(assemblyName, config.AssemblyName);
    }

    [Fact]
    public void Properties_HandleNullValues()
    {
        var config = new DatabaseProviderConfig();

        config.ProviderName = null!;
        config.FactoryType = null!;
        config.AssemblyPath = null!;
        config.AssemblyName = null!;

        Assert.Null(config.ProviderName);
        Assert.Null(config.FactoryType);
        Assert.Null(config.AssemblyPath);
        Assert.Null(config.AssemblyName);
    }

    [Fact]
    public void Properties_HandleWhitespaceValues()
    {
        var config = new DatabaseProviderConfig();

        config.ProviderName = "   ";
        config.FactoryType = "\t";
        config.AssemblyPath = "\n";
        config.AssemblyName = "\r\n";

        Assert.Equal("   ", config.ProviderName);
        Assert.Equal("\t", config.FactoryType);
        Assert.Equal("\n", config.AssemblyPath);
        Assert.Equal("\r\n", config.AssemblyName);
    }
}