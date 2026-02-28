using System;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class GenericDbDataSourceCoverageTests
{
    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GenericDbDataSource(null!, "Data Source=test.db"));
    }

    [Fact]
    public void Constructor_NullConnectionString_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        Assert.Throws<ArgumentNullException>(() => new GenericDbDataSource(factory, null!));
    }

    [Fact]
    public void ConnectionString_ReturnsConfiguredValue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var source = new GenericDbDataSource(factory, "Data Source=coverage.db");

        Assert.Equal("Data Source=coverage.db", source.ConnectionString);
    }

    [Fact]
    public void CreateConnection_WhenFactoryReturnsNull_Throws()
    {
        var source = new GenericDbDataSource(new NullConnectionFactory(), "Data Source=coverage.db");

        var ex = Assert.Throws<InvalidOperationException>(() => source.CreateConnection());
        Assert.Contains("null connection", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCommand_UsesBaseImplementation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var source = new GenericDbDataSource(factory, "Data Source=coverage.db");

        using var command = source.CreateCommand("SELECT 1");

        Assert.NotNull(command);
        Assert.Equal("SELECT 1", command.CommandText);
    }

    [Fact]
    public void OpenConnection_OpensAndReturnsConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var source = new GenericDbDataSource(factory, "Data Source=coverage.db");

        using var connection = source.OpenConnection();

        Assert.NotNull(connection);
        Assert.NotEqual(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task OpenConnectionAsync_OpensAndReturnsConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var source = new GenericDbDataSource(factory, "Data Source=coverage.db");

        await using var connection = await source.OpenConnectionAsync();

        Assert.NotNull(connection);
        Assert.NotEqual(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Dispose_And_DisposeAsync_AreNoOps()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var source = new GenericDbDataSource(factory, "Data Source=coverage.db");

        source.Dispose();
        await source.DisposeAsync();
    }

    private sealed class NullConnectionFactory : DbProviderFactory
    {
        public override DbConnection? CreateConnection()
        {
            return null;
        }
    }
}
