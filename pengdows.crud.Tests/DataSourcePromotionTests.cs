using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DataSourcePromotionTests
{
    // DEC-009: fakeDb's CreateDataSource threw unless SupportsNativeDataSource was set; 2.0.5 (no
    // override) and real providers without their own data source return .NET's default one.
    [Fact]
    public void FakeDbFactory_CreateDataSource_WithoutOptIn_ReturnsTheDefaultDataSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        using var dataSource = factory.CreateDataSource("Data Source=:memory:;EmulatedProduct=Sqlite");
        using var connection = dataSource.CreateConnection();

        Assert.IsType<fakeDbConnection>(connection);
        Assert.Same(typeof(DbProviderFactory).Assembly, dataSource.GetType().Assembly);
    }

    [Fact]
    public void FakeDbFactory_CreateDataSource_WithOptIn_ReturnsFakeDbDataSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { SupportsNativeDataSource = true };

        using var dataSource = factory.CreateDataSource("Data Source=:memory:;EmulatedProduct=Sqlite");

        Assert.IsType<FakeDbDataSource>(dataSource);
    }

    // A factory whose CreateDataSource only returns .NET's default has no native data source:
    // the context uses its generic wrapper, as for a provider with no override at all.
    [Fact]
    public void Initialization_FactoryReturningTheBaseDefaultDataSource_UsesTheGenericWrapper()
    {
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            EnableMetrics = false
        }, new BaseDataSourceFactory());

        Assert.IsType<GenericDbDataSource>(context.GetInternalDataSource());
    }

    private sealed class BaseDataSourceFactory : DbProviderFactory
    {
        private readonly fakeDbFactory _inner = new(SupportedDatabase.Sqlite);
        public override DbConnection CreateConnection() => _inner.CreateConnection();
        public override DbCommand CreateCommand() => _inner.CreateCommand();
        public override DbParameter CreateParameter() => _inner.CreateParameter();
        public override DbConnectionStringBuilder CreateConnectionStringBuilder() => _inner.CreateConnectionStringBuilder()!;
        public override DbDataSource CreateDataSource(string connectionString) => base.CreateDataSource(connectionString);
    }

    [Fact]
    public void Initialization_AlwaysCreatesDataSource()
    {
        // Arrange: fakeDbFactory does NOT implement CreateDataSource
        var factory = new fakeDbFactory(enums.SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = false
        };

        // Act
        var context = new DatabaseContext(config, factory);

        // Assert
        Assert.NotNull(context.GetInternalDataSource());
        // Should be our generic wrapper since fakeDbFactory doesn't have a native one
        Assert.IsType<GenericDbDataSource>(context.GetInternalDataSource());
    }

    [Fact]
    public void GenericDbDataSource_CreateConnection_SetsConnectionString()
    {
        // Arrange
        var factory = new fakeDbFactory(enums.SupportedDatabase.Sqlite);
        var expectedCs = "Data Source=test.db";
        var dataSource = new GenericDbDataSource(factory, expectedCs);

        // Act
        using var connection = dataSource.CreateConnection();

        // Assert
        Assert.Equal(expectedCs, connection.ConnectionString);
    }
}
