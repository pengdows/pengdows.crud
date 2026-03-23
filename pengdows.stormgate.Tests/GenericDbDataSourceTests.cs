using System.Data.Common;

namespace pengdows.stormgate.Tests;

public class GenericDbDataSourceTests
{
    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new GenericDbDataSource(null!, "cs"));
        Assert.Throws<ArgumentException>(() => new GenericDbDataSource(new MockFactory(), null!));
        Assert.Throws<ArgumentException>(() => new GenericDbDataSource(new MockFactory(), " "));
    }

    [Fact]
    public void CreateDbConnection_ReturnsConfiguredConnection()
    {
        // Arrange
        var mockConn = new Mock<DbConnection>();
        var factory = new MockFactory(mockConn.Object);
        var dataSource = new GenericDbDataSource(factory, "Server=myServer;Database=myDb;");

        // Act
        // Use reflection to call protected method
        var method = typeof(GenericDbDataSource).GetMethod("CreateDbConnection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var connection = (DbConnection)method!.Invoke(dataSource, null)!;

        // Assert
        Assert.Same(mockConn.Object, connection);
        mockConn.VerifySet(c => c.ConnectionString = "Server=myServer;Database=myDb;");
    }

    [Fact]
    public void CreateDbConnection_ThrowsIfFactoryReturnsNull()
    {
        // Arrange
        var factory = new MockFactory(null);
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
        var factory = new MockFactory();
        var dataSource = new GenericDbDataSource(factory, "cs");

        // Act
        dataSource.Dispose();
        await dataSource.DisposeAsync();

        // Assert - no exception
    }

    private class MockFactory : DbProviderFactory
    {
        private readonly DbConnection? _connection;

        public MockFactory(DbConnection? connection = null)
        {
            _connection = connection;
        }

        public override DbConnection? CreateConnection() => _connection;
    }
}
