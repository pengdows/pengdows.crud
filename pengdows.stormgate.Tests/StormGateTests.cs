namespace pengdows.stormgate.Tests;

public class StormGateTests
{
    private readonly Mock<DbDataSource> _mockDataSource = new();
    private readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new StormGate(null!, 1, _timeout));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StormGate(_mockDataSource.Object, 0, _timeout));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StormGate(_mockDataSource.Object, 1, TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public async Task OpenAsync_LogsOnTimeout()
    {
        // Arrange
        var mockLogger = new Mock<ILogger>();
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout, mockLogger.Object);

        // Take the only permit
        var mockConn = new Mock<DbConnection>();
        _mockDataSource.Protected().Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockConn.Object);
        await gate.OpenAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => gate.OpenAsync());

        mockLogger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("saturation")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task OpenAsync_UsesSemaphore()
    {
        // Arrange
        var mockConn = new Mock<DbConnection>();
        _mockDataSource.Protected().Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockConn.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);

        // Act
        using var conn1 = await gate.OpenAsync();

        // Assert
        Assert.NotNull(conn1);
        _mockDataSource.Protected().Verify("OpenDbConnectionAsync", Times.Once(), ItExpr.IsAny<CancellationToken>());

        // Try to open another one, it should timeout
        await Assert.ThrowsAsync<TimeoutException>(() => gate.OpenAsync());
    }

    [Fact]
    public async Task OpenAsync_ReleasesSemaphoreOnCatch()
    {
        // Arrange
        _mockDataSource.Protected().Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("BOOM"));

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.OpenAsync());

        // Should be able to try again (not timed out)
        _mockDataSource.Protected().Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new Mock<DbConnection>().Object);

        using var conn1 = await gate.OpenAsync();
        Assert.NotNull(conn1);
    }

    [Fact]
    public async Task OpenAsync_ReleasesOnConnectionDispose()
    {
        // Arrange
        var mockConn = new Mock<DbConnection>();
        _mockDataSource.Protected().Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockConn.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);

        // Act
        var conn1 = await gate.OpenAsync();
        conn1.Dispose();

        // Assert - should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task OpenAsync_ReleasesOnConnectionClose()
    {
        // Arrange
        var mockConn = new Mock<DbConnection>();
        _mockDataSource.Protected().Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockConn.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);

        // Act
        var conn1 = await gate.OpenAsync();
        conn1.Close();

        // Assert - should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public void Dispose_DisposesDataSourceAndSemaphore()
    {
        // Arrange
        var ds = new TestDataSource();
        var gate = new StormGate(ds, 1, _timeout);

        // Act
        gate.Dispose();

        // Assert
        Assert.True(ds.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesDataSourceAndSemaphore()
    {
        // Arrange
        var ds = new TestDataSource();
        var gate = new StormGate(ds, 1, _timeout);

        // Act
        await gate.DisposeAsync();

        // Assert
        Assert.True(ds.DisposedAsync);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // Arrange
        var ds = new TestDataSource();
        var gate = new StormGate(ds, 1, _timeout);

        // Act
        await gate.DisposeAsync();
        await gate.DisposeAsync();

        // Assert - no exception
    }

    [Fact]
    public void Create_ReturnsStormGate()
    {
        // Arrange
        var factory = new TestDataSourceResolverFactory();
        var cs = "key=Value";

        // Act
        using var gate = StormGate.Create(factory, cs, 5, _timeout);

        // Assert
        Assert.NotNull(gate);
    }

    [Fact]
    public async Task OpenAsync_ThrowsIfDisposed()
    {
        // Arrange
        var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        gate.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gate.OpenAsync());
    }

    [Fact]
    public async Task Dispose_WithActiveLease_DoesNotBreakLeaseDisposal()
    {
        var mockConn = new Mock<DbConnection>();
        _mockDataSource.Protected()
            .Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockConn.Object);

        var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        gate.Dispose();

        var ex = Record.Exception(conn.Dispose);

        Assert.Null(ex);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => gate.OpenAsync());
    }

    [Fact]
    public async Task OpenAsync_WhenProviderOpenIsCanceled_DoesNotLogError()
    {
        var mockLogger = new Mock<ILogger>();

        _mockDataSource.Protected()
            .Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException("canceled by provider"));

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout, mockLogger.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.OpenAsync());

        mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<OperationCanceledException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private class TestDataSource : DbDataSource
    {
        public bool Disposed { get; private set; }
        public bool DisposedAsync { get; private set; }

        public override string ConnectionString => "cs";

        protected override DbConnection CreateDbConnection() => throw new NotImplementedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        protected override ValueTask DisposeAsyncCore()
        {
            DisposedAsync = true;
            return base.DisposeAsyncCore();
        }
    }

    private class TestDataSourceResolverFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection() => new Mock<DbConnection>().Object;
        public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new DbConnectionStringBuilder();
    }

    // ── PermitTransaction savepoint delegation ──────────────────────────────

    [Fact]
    public async Task PermitTransaction_Savepoints_DelegateToInnerTransaction()
    {
        // Regression: PermitTransaction only overrode Commit/Rollback/IsolationLevel/Dispose —
        // Save(string)/Rollback(string)/Release(string)/SupportsSavepoints were never forwarded to
        // Inner, so callers using a StormGate-gated connection's transaction got DbTransaction's
        // base (unsupported) behavior instead of the wrapped provider transaction's real savepoint
        // support.
        var innerConnection = new SavepointCapableFakeConnection();
        innerConnection.Open();
        _mockDataSource.Protected()
            .Setup<ValueTask<DbConnection>>("OpenDbConnectionAsync", ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(innerConnection);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var permitConnection = await gate.OpenAsync();
        using var tx = permitConnection.BeginTransaction();

        Assert.True(tx.SupportsSavepoints);

        tx.Save("sp1");
        tx.Rollback("sp2");
        tx.Release("sp3");
        await tx.SaveAsync("sp4");
        await tx.RollbackAsync("sp5");
        await tx.ReleaseAsync("sp6");

        var innerTx = innerConnection.LastTransaction;
        Assert.NotNull(innerTx);
        Assert.Equal("sp1", innerTx!.SavedSavepointName);
        Assert.Equal("sp2", innerTx.RolledBackSavepointName);
        Assert.Equal("sp3", innerTx.ReleasedSavepointName);
        Assert.Equal("sp4", innerTx.SavedSavepointNameAsync);
        Assert.Equal("sp5", innerTx.RolledBackSavepointNameAsync);
        Assert.Equal("sp6", innerTx.ReleasedSavepointNameAsync);
    }

    private sealed class SavepointCapableFakeConnection : fakeDbConnection
    {
        public SavepointTrackingTransaction? LastTransaction { get; private set; }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            LastTransaction = new SavepointTrackingTransaction(this);
            return LastTransaction;
        }
    }

    private sealed class SavepointTrackingTransaction : DbTransaction
    {
        private readonly DbConnection _connection;

        public SavepointTrackingTransaction(DbConnection connection)
        {
            _connection = connection;
        }

        protected override DbConnection DbConnection => _connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
        public override bool SupportsSavepoints => true;
        public string? SavedSavepointName { get; private set; }
        public string? RolledBackSavepointName { get; private set; }
        public string? ReleasedSavepointName { get; private set; }
        public string? SavedSavepointNameAsync { get; private set; }
        public string? RolledBackSavepointNameAsync { get; private set; }
        public string? ReleasedSavepointNameAsync { get; private set; }

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }

        public override void Save(string savepointName) => SavedSavepointName = savepointName;
        public override void Rollback(string savepointName) => RolledBackSavepointName = savepointName;
        public override void Release(string savepointName) => ReleasedSavepointName = savepointName;

        public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            SavedSavepointNameAsync = savepointName;
            return Task.CompletedTask;
        }

        public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            RolledBackSavepointNameAsync = savepointName;
            return Task.CompletedTask;
        }

        public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            ReleasedSavepointNameAsync = savepointName;
            return Task.CompletedTask;
        }
    }
}
