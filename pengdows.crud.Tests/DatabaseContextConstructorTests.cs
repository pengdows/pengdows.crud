#region

using System;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextConstructorTests
{
    [Fact]
    public void Constructor_With_ConnectionString_And_ProviderName_Should_Create_Context()
    {
        // Arrange
        var connectionString = "Server=test;Database=testdb;";
        var providerName = "Microsoft.Data.SqlClient";
        
        // Register the factory first (simulating real scenario)
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        DbProviderFactories.RegisterFactory(providerName, factory);
        
        // Act
        var context = new DatabaseContext(connectionString, providerName);
        
        // Assert
        Assert.NotNull(context);
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        Assert.Equal(ReadWriteMode.ReadWrite, context.ReadWriteMode);
    }

    [Fact]
    public void Constructor_With_ConnectionString_And_ProviderName_With_TypeMap_Should_Create_Context()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=testdb;";
        var providerName = "Npgsql";
        var typeMap = new TypeMapRegistry();
        
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        DbProviderFactories.RegisterFactory(providerName, factory);
        
        // Act
        var context = new DatabaseContext(connectionString, providerName, typeMap);
        
        // Assert
        Assert.NotNull(context);
        Assert.Same(typeMap, context.TypeMapRegistry);
    }

    [Fact]
    public void Constructor_With_ConnectionString_And_ProviderName_With_All_Parameters_Should_Create_Context()
    {
        // Arrange
        var connectionString = "Data Source=:memory:;";
        var providerName = "Microsoft.Data.Sqlite";
        var typeMap = new TypeMapRegistry();
        var mode = DbMode.SingleConnection;
        var readWriteMode = ReadWriteMode.ReadOnly;
        var loggerFactory = NullLoggerFactory.Instance;
        
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        DbProviderFactories.RegisterFactory(providerName, factory);
        
        // Act
        var context = new DatabaseContext(connectionString, providerName, typeMap, mode, readWriteMode, loggerFactory);
        
        // Assert
        Assert.NotNull(context);
        Assert.Equal(mode, context.ConnectionMode);
        Assert.Equal(readWriteMode, context.ReadWriteMode);
        Assert.Same(typeMap, context.TypeMapRegistry);
    }

    [Fact]
    public void Constructor_With_Invalid_ProviderName_Should_Throw()
    {
        // Arrange
        var connectionString = "Server=test;";
        var invalidProviderName = "NonExistent.Provider";
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new DatabaseContext(connectionString, invalidProviderName));
    }

    [Fact]
    public void Constructor_With_Null_ConnectionString_Should_Throw()
    {
        // Arrange
        string nullConnectionString = null!;
        var providerName = "System.Data.SqlClient";
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DatabaseContext(nullConnectionString, providerName));
    }

    [Fact]
    public void Constructor_With_Empty_ConnectionString_Should_Throw()
    {
        // Arrange
        var emptyConnectionString = "";
        var providerName = "System.Data.SqlClient";
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new DatabaseContext(emptyConnectionString, providerName));
    }

    [Fact]
    public void Constructor_With_Null_ProviderName_Should_Throw()
    {
        // Arrange
        var connectionString = "Server=test;";
        string nullProviderName = null!;
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DatabaseContext(connectionString, nullProviderName));
    }

    [Fact]
    public void Constructor_With_Configuration_Should_Create_Context()
    {
        // Arrange
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=configtest;Database=testdb;EmulatedProduct=SqlServer",
            ProviderName = "Microsoft.Data.SqlClient",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var loggerFactory = NullLoggerFactory.Instance;
        var typeMap = new TypeMapRegistry();
        
        // Act
        var context = new DatabaseContext(config, factory, loggerFactory, typeMap);
        
        // Assert
        Assert.NotNull(context);
        // Full server (SqlServer) must coerce to Standard (KeepAlive reserved for LocalDb)
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        Assert.Equal(ReadWriteMode.ReadWrite, context.ReadWriteMode);
        Assert.Same(typeMap, context.TypeMapRegistry);
    }

    [Fact]
    public void Constructor_With_Configuration_And_Null_Factory_Should_Throw()
    {
        // Arrange
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;",
            ProviderName = "Microsoft.Data.SqlClient"
        };
        
        DbProviderFactory nullFactory = null!;
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DatabaseContext(config, nullFactory, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Constructor_With_Null_Configuration_Should_Throw()
    {
        // Arrange
        DatabaseContextConfiguration nullConfig = null!;
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DatabaseContext(nullConfig, factory, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Constructor_With_Configuration_Missing_ConnectionString_Should_Throw()
    {
        // Arrange
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = null!, // Missing connection string
            ProviderName = "Microsoft.Data.SqlClient"
        };
        
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => 
            new DatabaseContext(config, factory, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Constructor_With_Different_DbModes_Should_Create_Context()
    {
        // Test all DbMode values
        var modes = new[] 
        { 
            DbMode.Standard, 
            DbMode.KeepAlive, 
            DbMode.SingleWriter, 
            DbMode.SingleConnection 
        };
        
        foreach (var mode in modes)
        {
            // Arrange
            var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=:memory:",
                DbMode = mode
            };

            // Act
            var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

            // Assert: :memory: must always coerce to SingleConnection
            Assert.NotNull(context);
            Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        }
    }

    [Fact]
    public void Constructor_With_Different_ReadWriteModes_Should_Create_Context()
    {
        // Test all ReadWriteMode values
        var modes = new[] 
        { 
            ReadWriteMode.ReadOnly, 
            ReadWriteMode.ReadWrite 
        };
        
        foreach (var mode in modes)
        {
            // Arrange
            var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = "Host=localhost;Database=test;",
                ReadWriteMode = mode
            };
            
            // Act
            var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
            
            // Assert
            Assert.NotNull(context);
            Assert.Equal(mode, context.ReadWriteMode);
        }
    }

    [Fact]
    public void Constructor_Should_Initialize_Default_Values()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;"
        };
        
        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        
        // Assert
        Assert.NotNull(context);
        Assert.Equal(DbMode.Standard, context.ConnectionMode); // Default
        Assert.Equal(ReadWriteMode.ReadWrite, context.ReadWriteMode); // Default
        Assert.NotNull(context.TypeMapRegistry); // Should create default
        Assert.True(context.NumberOfOpenConnections >= 0);
        Assert.True(context.MaxNumberOfConnections >= 0);
    }

    [Fact]
    public void Constructor_With_Custom_TypeMapRegistry_Should_Use_Provided_Registry()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var customTypeMap = new TypeMapRegistry();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=mysql;Database=test;"
        };
        
        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance, customTypeMap);
        
        // Assert
        Assert.NotNull(context);
        Assert.Same(customTypeMap, context.TypeMapRegistry);
    }

    [Fact]
    public void Constructor_With_Null_LoggerFactory_Should_Use_NullLogger()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=oracle;"
        };
        
        // Act
        var context = new DatabaseContext(config, factory);
        
        // Assert
        Assert.NotNull(context);
        // Should not throw and should handle null logger gracefully
    }

    [Fact]
    public void Constructor_Should_Set_Context_Properties_Correctly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Database=test.fdb;",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        
        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        
        // Assert
        Assert.NotNull(context);
        Assert.Equal(SupportedDatabase.Firebird, context.Product);
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        Assert.Equal(ReadWriteMode.ReadOnly, context.ReadWriteMode);
    }

    [Fact]
    public void Constructor_Should_Handle_Complex_Connection_Strings()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var complexConnectionString = "Host=localhost;Port=5432;Database=testdb;Username=user;Password=pass;Pooling=true;MinPoolSize=5;MaxPoolSize=100;";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = complexConnectionString
        };
        
        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        
        // Assert
        Assert.NotNull(context);
        // Should handle complex connection strings without throwing
    }

    [Fact]
    public void Constructor_Should_Initialize_Connection_Counters()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=testdb;EmulatedProduct=SqlServer", // Use SQL Server which forces Standard mode
            DbMode = DbMode.Standard // Explicitly set Standard mode
        };
        
        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        
        // Assert
        // For SQL Server in Standard mode, counters should be 0 after initialization connection is disposed
        Assert.Equal(0, context.NumberOfOpenConnections); // Should start at 0 after initialization reset
        Assert.Equal(0, context.MaxNumberOfConnections); // Should start at 0 after initialization reset
    }
}
