namespace pengdows.stormgate.Tests;

public class PermitConnectionTests
{
    private readonly Mock<DbDataSource> _mockDataSource = new();
    private readonly Mock<DbConnection> _mockInner = new();
    private readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(50);

    public PermitConnectionTests()
    {
        _mockDataSource.Protected().Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(_mockInner.Object);
    }

    [Fact]
    public async Task Delegation_Works()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();

        // Act & Assert
        _mockInner.SetupGet(c => c.ConnectionString).Returns("cs");
        Assert.Equal("cs", conn.ConnectionString);

        conn.ConnectionString = "new_cs";
        _mockInner.VerifySet(c => c.ConnectionString = "new_cs");

        _mockInner.SetupGet(c => c.Database).Returns("db");
        Assert.Equal("db", conn.Database);

        _mockInner.SetupGet(c => c.DataSource).Returns("ds");
        Assert.Equal("ds", conn.DataSource);

        _mockInner.SetupGet(c => c.ServerVersion).Returns("1.0");
        Assert.Equal("1.0", conn.ServerVersion);

        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        Assert.Equal(ConnectionState.Open, conn.State);

        conn.ChangeDatabase("new_db");
        _mockInner.Verify(c => c.ChangeDatabase("new_db"));

        _mockInner.Protected().Setup<DbTransaction>("BeginDbTransaction", IsolationLevel.ReadCommitted).Returns(new Mock<DbTransaction>().Object);
        conn.BeginTransaction(IsolationLevel.ReadCommitted);
        _mockInner.Protected().Verify("BeginDbTransaction", Times.Once(), IsolationLevel.ReadCommitted);

        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(new Mock<DbCommand>().Object);
        conn.CreateCommand();
        _mockInner.Protected().Verify("CreateDbCommand", Times.Once());
    }

    [Fact]
    public async Task CloseAsync_CallsInnerAndReleasesPermit()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);

        // Act
        await conn.CloseAsync();

        // Assert
        _mockInner.Verify(c => c.CloseAsync(), Times.Once);
        
        // Should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task GuardMethods_ThrowWhenClosed()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Closed);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => conn.BeginTransaction());
        Assert.Throws<InvalidOperationException>(() => conn.CreateCommand());
    }

    [Fact]
    public async Task Open_Throws()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => conn.Open());
        await Assert.ThrowsAsync<InvalidOperationException>(() => conn.OpenAsync());
    }

    [Fact]
    public async Task Close_CallsInnerAndReleasesPermit()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);

        // Act
        conn.Close();

        // Assert
        _mockInner.Verify(c => c.Close(), Times.Once);
        
        // Should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task Dispose_CallsInnerAndReleasesPermit()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Act
        conn.Dispose();

        // Assert
        _mockInner.Protected().Verify("Dispose", Times.Once(), ItExpr.IsAny<bool>());

        // Should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task DisposeAsync_CallsInnerAndReleasesPermit()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Act
        await conn.DisposeAsync();

        // Assert
        _mockInner.Verify(c => c.DisposeAsync(), Times.Once);

        // Should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task ReleasePermitOnce_IsIdempotent()
    {
        // Arrange
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Act
        conn.Close();
        conn.Dispose();
        await conn.DisposeAsync();

        // Assert - should still only have released once (max capacity is 1)
        var conn2 = await gate.OpenAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => gate.OpenAsync());
    }
}
