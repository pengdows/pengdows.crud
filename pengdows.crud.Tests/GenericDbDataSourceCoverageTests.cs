using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
    public void GenericDbDataSource_DoesNotDeclareRedundantCreateDbCommandOverride()
    {
        var declared = typeof(GenericDbDataSource).GetMethod(
            "CreateDbCommand",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.Null(declared);
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

    [Fact]
    public void OpenConnection_WhenOpenThrows_DisposesConnection()
    {
        var factory = new ThrowingOpenFactory();
        var source = new GenericDbDataSource(factory, "Data Source=coverage.db");

        Assert.Throws<InvalidOperationException>(() => source.OpenConnection());
        Assert.True(factory.LastConnection!.WasDisposed);
    }

    [Fact]
    public async Task OpenConnectionAsync_WhenOpenThrows_DisposesConnection()
    {
        var factory = new ThrowingOpenFactory();
        var source = new GenericDbDataSource(factory, "Data Source=coverage.db");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.OpenConnectionAsync());
        Assert.True(factory.LastConnection!.WasDisposed);
    }

    private sealed class NullConnectionFactory : DbProviderFactory
    {
        public override DbConnection? CreateConnection()
        {
            return null;
        }
    }

    private sealed class ThrowingOpenFactory : DbProviderFactory
    {
        public ThrowingConnection? LastConnection { get; private set; }

        public override DbConnection CreateConnection()
        {
            LastConnection = new ThrowingConnection();
            return LastConnection;
        }
    }

    private sealed class ThrowingConnection : DbConnection
    {
        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) { }

        public override void Open()
        {
            throw new InvalidOperationException("open failed");
        }

        public override Task OpenAsync(System.Threading.CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("open failed");
        }

        public override void Close() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
