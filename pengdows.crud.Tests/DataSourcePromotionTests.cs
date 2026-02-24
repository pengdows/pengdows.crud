using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DataSourcePromotionTests
{
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
        Assert.NotNull(context.DataSource);
        // Should be our generic wrapper since fakeDbFactory doesn't have a native one
        Assert.IsType<GenericDbDataSource>(context.DataSource);
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
