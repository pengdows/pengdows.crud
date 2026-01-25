#region

using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ProviderName = providerName
        };
        var context = new DatabaseContext(config, DbProviderFactories.GetFactory(providerName));

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
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ProviderName = providerName
        };
        var context = new DatabaseContext(config, DbProviderFactories.GetFactory(providerName),
            NullLoggerFactory.Instance, typeMap);

        // Assert
        Assert.NotNull(context);
        Assert.Same(typeMap, context.TypeMapRegistry);
    }

    [Fact]
    public void Constructor_With_ConnectionString_And_ProviderName_With_All_Parameters_Should_Create_Context()
    {
        // Arrange
        var connectionString = "Data Source=file.db;";
        var providerName = "Microsoft.Data.Sqlite";
        var typeMap = new TypeMapRegistry();
        var mode = DbMode.SingleConnection;
        var readWriteMode = ReadWriteMode.ReadWrite;
        var loggerFactory = NullLoggerFactory.Instance;

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        DbProviderFactories.RegisterFactory(providerName, factory);

        // Act
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ProviderName = providerName,
            DbMode = mode,
            ReadWriteMode = readWriteMode
        };
        var context = new DatabaseContext(config, DbProviderFactories.GetFactory(providerName), loggerFactory, typeMap);

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
        var invalidProviderName = "NonExistent.Provider";

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            DbProviderFactories.GetFactory(invalidProviderName));
    }

    [Fact]
    public void Constructor_With_Null_ConnectionString_Should_Throw()
    {
        // Arrange
        string nullConnectionString = null!;
        var providerName = "System.Data.SqlClient";

        // Act & Assert
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = nullConnectionString,
            ProviderName = providerName
        };
        Assert.Throws<ArgumentException>(() =>
            new DatabaseContext(config, factory));
    }

    [Fact]
    public void Constructor_With_Empty_ConnectionString_Should_Throw()
    {
        // Arrange
        var emptyConnectionString = "";
        var providerName = "System.Data.SqlClient";

        // Act & Assert
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = emptyConnectionString,
            ProviderName = providerName
        };
        Assert.Throws<ArgumentException>(() =>
            new DatabaseContext(config, factory));
    }

    [Fact]
    public void Constructor_With_Null_ProviderName_Should_Throw()
    {
        // Arrange
        string nullProviderName = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            DbProviderFactories.GetFactory(nullProviderName));
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
        // Users can force KeepAlive on full servers for testing (even though less functional)
        Assert.Equal(DbMode.KeepAlive, context.ConnectionMode);
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
        var complexConnectionString =
            "Host=localhost;Port=5432;Database=testdb;Username=user;Password=pass;Pooling=true;MinPoolSize=5;MaxPoolSize=100;";
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
            ConnectionString =
                "Server=test;Database=testdb;EmulatedProduct=SqlServer", // Use SQL Server which forces Standard mode
            DbMode = DbMode.Standard // Explicitly set Standard mode
        };

        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Assert
        // For SQL Server in Standard mode, counters should be 0 after initialization connection is disposed
        Assert.Equal(0, context.NumberOfOpenConnections); // Should start at 0 after initialization reset
        Assert.Equal(0, context.MaxNumberOfConnections); // Should start at 0 after initialization reset
    }

    [Fact]
    public void Constructor_Adds_Default_MinPoolSize_For_Standard_Mode_When_Missing()
    {
        // Arrange
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=testdb;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);

        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Assert
        var builder = new DbConnectionStringBuilder { ConnectionString = context.ConnectionString };
        Assert.True(builder.ContainsKey("Min Pool Size"));
        Assert.Equal("1", builder["Min Pool Size"].ToString());
    }

    [Fact]
    public void Constructor_Does_Not_Override_Existing_MinPoolSize()
    {
        // Arrange
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=testdb;Min Pool Size=3;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);

        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Assert
        var builder = new DbConnectionStringBuilder { ConnectionString = context.ConnectionString };
        Assert.True(builder.ContainsKey("Min Pool Size"));
        Assert.Equal("3", builder["Min Pool Size"].ToString());
    }

    [Fact]
    public void Constructor_Skips_MinPoolSize_When_Pooling_Disabled()
    {
        // Arrange
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=testdb;Pooling=false;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);

        // Act
        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Assert
        var builder = new DbConnectionStringBuilder { ConnectionString = context.ConnectionString };
        Assert.False(builder.ContainsKey("Min Pool Size"));
    }

    private sealed class RejectingBuilderFactory : DbProviderFactory
    {
        private sealed class RejectingBuilder : DbConnectionStringBuilder
        {
            [AllowNull]
            public override object this[string keyword]
            {
                get => base[keyword];
                set
                {
                    if (keyword.Equals("EmulatedProduct", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("Unrecognized keyword.");
                    }

                    base[keyword] = value ?? string.Empty;
                }
            }
        }

        public override DbConnection CreateConnection()
        {
            return new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            return new RejectingBuilder();
        }
    }

    [Fact]
    public void Constructor_Preserves_ConnectionString_When_Builder_Rejects_Custom_Keys()
    {
        // Arrange - include custom keyword that real providers may reject
        const string rawConnectionString = "Server=test;Database=testdb;EmulatedProduct=SqlServer";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = rawConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var factory = new RejectingBuilderFactory();

        // Act
        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Assert - the original connection string should be retained verbatim
        Assert.Equal(rawConnectionString, context.ConnectionString);
    }

    [Fact]
    public void Constructor_With_DbDataSource_Should_Create_Context()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=testdb;";
        var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString
        };

        // Act
        var context = new DatabaseContext(config, dataSource, dataSource.Factory);

        // Assert
        Assert.NotNull(context);
        Assert.Same(dataSource, context.DataSource);
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        Assert.Equal(ReadWriteMode.ReadWrite, context.ReadWriteMode);
    }

    [Fact]
    public void Constructor_With_DbDataSource_And_TypeMap_Should_Create_Context()
    {
        // Arrange
        var connectionString = "Data Source=test.db;";
        var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.Sqlite);
        var typeMap = new TypeMapRegistry();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString
        };

        // Act
        var context = new DatabaseContext(config, dataSource, dataSource.Factory, NullLoggerFactory.Instance, typeMap);

        // Assert
        Assert.NotNull(context);
        Assert.Same(dataSource, context.DataSource);
        Assert.Same(typeMap, context.TypeMapRegistry);
    }

    [Fact]
    public void Constructor_With_DbDataSource_And_All_Parameters_Should_Create_Context()
    {
        // Arrange
        var connectionString = "Server=test;Database=testdb;";
        var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.SqlServer);
        var typeMap = new TypeMapRegistry();
        var mode = DbMode.KeepAlive;
        var readWriteMode = ReadWriteMode.ReadOnly;
        var loggerFactory = NullLoggerFactory.Instance;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = mode,
            ReadWriteMode = readWriteMode
        };

        // Act
        var context = new DatabaseContext(config, dataSource, dataSource.Factory, loggerFactory, typeMap);

        // Assert
        Assert.NotNull(context);
        Assert.Same(dataSource, context.DataSource);
        Assert.Equal(mode, context.ConnectionMode);
        Assert.Equal(readWriteMode, context.ReadWriteMode);
        Assert.Same(typeMap, context.TypeMapRegistry);
    }

    [Fact]
    public void Constructor_With_Null_DbDataSource_Should_Throw()
    {
        // Arrange
        DbDataSource nullDataSource = null!;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;"
        };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseContext(config, nullDataSource, new fakeDbFactory(SupportedDatabase.PostgreSql),
                NullLoggerFactory.Instance));
    }

    [Fact]
    public void Constructor_With_DbDataSource_Should_Create_Connections_From_DataSource()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=testdb;";
        var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString
        };

        // Act
        using var context = new DatabaseContext(config, dataSource, dataSource.Factory, NullLoggerFactory.Instance);
        using var connection = context.GetConnection(ExecutionType.Read);

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(connectionString, connection.ConnectionString);
    }

    [Fact]
    public void Constructor_With_DbDataSource_Should_Detect_Dialect()
    {
        // Arrange
        var connectionString = "Server=test;Database=testdb;";
        var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString
        };

        // Act
        using var context = new DatabaseContext(config, dataSource, dataSource.Factory, NullLoggerFactory.Instance);

        // Assert
        Assert.NotNull(context.Dialect);
        Assert.Equal(SupportedDatabase.SqlServer, context.Product);
    }

    [Fact]
    public void Constructor_With_DbDataSource_Should_Initialize_Default_Values()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;";
        var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString
        };

        // Act
        using var context = new DatabaseContext(config, dataSource, dataSource.Factory, NullLoggerFactory.Instance);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(DbMode.Standard, context.ConnectionMode); // Default
        Assert.Equal(ReadWriteMode.ReadWrite, context.ReadWriteMode); // Default
        Assert.NotNull(context.TypeMapRegistry); // Should create default
        Assert.True(context.NumberOfOpenConnections >= 0);
        Assert.True(context.MaxNumberOfConnections >= 0);
    }
}