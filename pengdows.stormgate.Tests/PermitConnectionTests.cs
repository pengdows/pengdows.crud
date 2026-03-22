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

    // P2: Open/OpenAsync should be a no-op when already open (Dapper/EF Core call Open defensively)
    [Fact]
    public async Task Open_WhenAlreadyOpen_IsNoOp()
    {
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);

        var ex = Record.Exception(() => conn.Open());
        Assert.Null(ex);

        var exAsync = await Record.ExceptionAsync(() => conn.OpenAsync());
        Assert.Null(exAsync);
    }

    // P2: Open/OpenAsync when connection is not open should throw NotSupportedException
    [Fact]
    public async Task Open_WhenNotOpen_ThrowsNotSupportedException()
    {
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        // Default mock State is ConnectionState.Closed (0)

        Assert.Throws<NotSupportedException>(() => conn.Open());
        await Assert.ThrowsAsync<NotSupportedException>(() => conn.OpenAsync());
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

    // P0: Dispose must call Close (via base.Dispose) BEFORE Dispose on the inner connection,
    // so the inner is closed cleanly before being torn down. Some providers are not idempotent
    // if Close() is called after Dispose().
    [Fact]
    public async Task Dispose_ClosesInnerBeforeDisposingIt_WhenConnectionIsOpen()
    {
        var callOrder = new List<string>();
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.Close()).Callback(() => callOrder.Add("Close"));
        _mockInner.Protected()
            .Setup("Dispose", ItExpr.IsAny<bool>())
            .Callback<bool>(_ => callOrder.Add("Dispose"));

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        conn.Dispose();

        Assert.Contains("Close", callOrder);
        Assert.Contains("Dispose", callOrder);
        Assert.True(
            callOrder.IndexOf("Close") < callOrder.IndexOf("Dispose"),
            $"Expected Close before Dispose but got: [{string.Join(", ", callOrder)}]");
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
    public async Task DisposeAsync_ClosesInnerBeforeDisposingIt_WhenConnectionIsOpen()
    {
        var callOrder = new List<string>();
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.CloseAsync()).Callback(() => callOrder.Add("CloseAsync"));
        _mockInner.Setup(c => c.DisposeAsync()).Callback(() => callOrder.Add("DisposeAsync"));

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        await conn.DisposeAsync();

        Assert.Contains("CloseAsync", callOrder);
        Assert.Contains("DisposeAsync", callOrder);
        Assert.True(
            callOrder.IndexOf("CloseAsync") < callOrder.IndexOf("DisposeAsync"),
            $"Expected CloseAsync before DisposeAsync but got: [{string.Join(", ", callOrder)}]");
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

    // P1: ThrowIfInnerClosed must check _released, not just inner.State.
    // If Close() throws, the permit is still released (finally block), but State may remain Open.
    // CreateCommand/BeginTransaction must not succeed on a connection whose permit was returned.
    [Fact]
    public async Task CreateCommand_ThrowsAfterPermitReleased_EvenIfInnerStateIsStillOpen()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.Close()).Throws(new InvalidOperationException("provider close failed"));
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(new Mock<DbCommand>().Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Close throws, but the finally block in Close() still releases the permit (_released = 1)
        Assert.Throws<InvalidOperationException>(() => conn.Close());

        // _released is now 1, inner.State is still Open — guard must fire
        Assert.Throws<InvalidOperationException>(() => conn.CreateCommand());
    }

    [Fact]
    public async Task BeginTransaction_ThrowsAfterPermitReleased_EvenIfInnerStateIsStillOpen()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.Close()).Throws(new InvalidOperationException("provider close failed"));
        _mockInner.Protected()
            .Setup<DbTransaction>("BeginDbTransaction", IsolationLevel.Unspecified)
            .Returns(new Mock<DbTransaction>().Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        Assert.Throws<InvalidOperationException>(() => conn.Close());

        // Permit returned; guard must fire regardless of inner.State
        Assert.Throws<InvalidOperationException>(() => conn.BeginTransaction());
    }

    // Minor: BeginDbTransactionAsync must override to call the inner's async path, not fall
    // back to the sync BeginDbTransaction default in DbConnection.
    [Fact]
    public async Task BeginTransactionAsync_DelegatesToInnerAsync()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockTx = new Mock<DbTransaction>();
        _mockInner.Protected()
            .Setup<ValueTask<DbTransaction>>("BeginDbTransactionAsync",
                IsolationLevel.ReadCommitted,
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockTx.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();

        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        Assert.Same(mockTx.Object, tx);
        _mockInner.Protected().Verify("BeginDbTransactionAsync",
            Times.Once(),
            IsolationLevel.ReadCommitted,
            ItExpr.IsAny<CancellationToken>());
    }
}
