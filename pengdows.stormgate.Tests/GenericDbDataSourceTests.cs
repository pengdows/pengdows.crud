using System.Data.Common;

namespace pengdows.stormgate.Tests;

public class GenericDbDataSourceTests
{
    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new GenericDbDataSource(null!, "cs"));
        Assert.Throws<ArgumentException>(() => new GenericDbDataSource(new StubFactory(), null!));
        Assert.Throws<ArgumentException>(() => new GenericDbDataSource(new StubFactory(), " "));
    }

    [Fact]
    public void CreateDbConnection_ReturnsConfiguredConnection()
    {
        // Arrange
        var inner = new fakeDbConnection();
        var factory = new StubFactory(inner);
        var dataSource = new GenericDbDataSource(factory, "Server=myServer;Database=myDb;");

        // Act
        // Use reflection to call protected method
        var method = typeof(GenericDbDataSource).GetMethod("CreateDbConnection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var connection = (DbConnection)method!.Invoke(dataSource, null)!;

        // Assert
        Assert.Same(inner, connection);
        Assert.Contains("myServer", inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDbConnection_ThrowsIfFactoryReturnsNull()
    {
        // Arrange
        var factory = new StubFactory(null);
        var dataSource = new GenericDbDataSource(factory, "cs");

        // Act & Assert
        var method = typeof(GenericDbDataSource).GetMethod("CreateDbConnection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method!.Invoke(dataSource, null));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public async Task Dispose_Works()
    {
        // Arrange
        var factory = new StubFactory();
        var dataSource = new GenericDbDataSource(factory, "cs");

        // Act
        dataSource.Dispose();
        await dataSource.DisposeAsync();

        // Assert - no exception
    }

    private class StubFactory : DbProviderFactory
    {
        private readonly DbConnection? _connection;

        public StubFactory(DbConnection? connection = null)
        {
            _connection = connection;
        }

        public override DbConnection? CreateConnection() => _connection;
    }
}
