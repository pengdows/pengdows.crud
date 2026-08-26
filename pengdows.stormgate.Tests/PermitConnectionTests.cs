using System.Reflection;

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

    // P1-1: Dispose must call _inner.Dispose() even when Close() throws.
    // ReleasePermitOnce() is still invoked via Close's finally block.
    [Fact]
    public async Task Dispose_StillDisposesInner_WhenCloseThrows()
    {
        var innerDisposed = false;
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.Close()).Throws(new InvalidOperationException("provider close failed"));
        _mockInner.Protected()
            .Setup("Dispose", ItExpr.IsAny<bool>())
            .Callback<bool>(_ => innerDisposed = true);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Exception from Close() may propagate, but inner.Dispose must still run.
        _ = Record.Exception(() => conn.Dispose());

        Assert.True(innerDisposed, "Inner connection must be disposed even when Close() throws.");
    }

    // P1-1: DisposeAsync must call _inner.DisposeAsync() even when CloseAsync() throws.
    // The permit must also be released.
    [Fact]
    public async Task DisposeAsync_StillDisposesInner_WhenCloseAsyncThrows()
    {
        var innerDisposed = false;
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.CloseAsync())
            .Returns(Task.FromException(new InvalidOperationException("provider close failed")));
        _mockInner.Setup(c => c.DisposeAsync())
            .Callback(() => innerDisposed = true)
            .Returns(ValueTask.CompletedTask);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Exception from CloseAsync() may propagate, but inner.DisposeAsync must still run.
        _ = await Record.ExceptionAsync(async () => await conn.DisposeAsync());

        Assert.True(innerDisposed, "Inner connection must be disposed even when CloseAsync() throws.");
    }

    // P1-1: DisposeAsync permit must be released even when CloseAsync() throws.
    [Fact]
    public async Task DisposeAsync_ReleasesPermit_WhenCloseAsyncThrows()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.CloseAsync())
            .Returns(Task.FromException(new InvalidOperationException("provider close failed")));
        _mockInner.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        _ = await Record.ExceptionAsync(async () => await conn.DisposeAsync());

        // Permit must be back in the gate — should be able to open a new connection.
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    // P2-1: ThrowIfInnerClosed first branch (_released) must produce a distinct message
    // from the inner.State == Closed branch so callers can distinguish the two conditions.
    [Fact]
    public async Task CreateCommand_ThrowsPermitReleasedMessage_AfterPermitReleased_WhileInnerIsStillOpen()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.Close()).Throws(new InvalidOperationException("provider close failed"));
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(new Mock<DbCommand>().Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Force _released = 1 via Close()'s finally
        _ = Record.Exception(() => conn.Close());

        // _released == 1, inner.State == Open — the error must NOT say "Connection is closed."
        // because that's misleading when the inner is still open. It should say the permit was released.
        var ex = Assert.Throws<InvalidOperationException>(() => conn.CreateCommand());
        Assert.Contains("permit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // P2-3: Open()/OpenAsync() on a disposed connection must throw ObjectDisposedException,
    // not the "cannot be opened directly" NotSupportedException.
    [Fact]
    public async Task Open_AfterDispose_ThrowsObjectDisposedException()
    {
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();
        conn.Dispose();

        Assert.Throws<ObjectDisposedException>(() => conn.Open());
    }

    [Fact]
    public async Task OpenAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();
        conn.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => conn.OpenAsync());
    }

    [Fact]
    public async Task CreateCommand_AfterDispose_ThrowsObjectDisposedException()
    {
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();
        conn.Dispose();

        Assert.Throws<ObjectDisposedException>(() => conn.CreateCommand());
    }

    [Fact]
    public async Task BeginTransaction_AfterDispose_ThrowsObjectDisposedException()
    {
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();
        conn.Dispose();

        Assert.Throws<ObjectDisposedException>(() => conn.BeginTransaction());
    }

    // Regression: Dispose(bool=false) is the finalizer path. If Close() throws and the
    // PermitConnection is not explicitly disposed (e.g. after conn.Close() throws), the GC
    // finalizer eventually calls Dispose(false). Exceptions from finalizers crash the process
    // on .NET 10+. The finalizer path must never call Close().
    [Fact]
    public async Task FinalizerPath_Dispose_DoesNotCallClose()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Setup(c => c.Close()).Throws(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Invoke Dispose(false) via reflection to simulate the GC finalizer path.
        // Must not throw — an exception from a finalizer crashes the process.
        var disposeMethod = conn.GetType().GetMethod(
            "Dispose",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(bool) }, null);
        Assert.NotNull(disposeMethod);

        var ex = Record.Exception(() => disposeMethod!.Invoke(conn, new object[] { false }));
        Assert.Null(ex);

        // Close must NOT have been called from the finalizer path.
        _mockInner.Verify(c => c.Close(), Times.Never());
    }

    // Item from review: the finalizer path (Dispose(false)) already avoids touching _inner
    // (per the comment above it) but never released the StormGate permit either — a
    // PermitConnection abandoned without an explicit Close/Dispose (dropped, or simply never
    // disposed by the caller) would hold its permit forever once the GC finalizer ran,
    // permanently shrinking the shared gate's budget by one slot per occurrence. Reuses the
    // same Dispose(false)-via-reflection technique as FinalizerPath_Dispose_DoesNotCallClose
    // above to simulate the finalizer deterministically instead of relying on real GC timing.
    [Fact]
    public async Task FinalizerPath_Dispose_StillReleasesPermit()
    {
        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        var conn = await gate.OpenAsync();

        var disposeMethod = conn.GetType().GetMethod(
            "Dispose",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(bool) }, null);
        Assert.NotNull(disposeMethod);

        var ex = Record.Exception(() => disposeMethod!.Invoke(conn, new object[] { false }));
        Assert.Null(ex);

        // Permit must be back in the gate — should be able to open a new connection.
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
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

        Assert.NotSame(mockTx.Object, tx);
        Assert.Same(conn, tx.Connection);
        _mockInner.Protected().Verify("BeginDbTransactionAsync",
            Times.Once(),
            IsolationLevel.ReadCommitted,
            ItExpr.IsAny<CancellationToken>());
    }

    // P0: PermitCommand's async execute methods must delegate to the inner command's real
    // async implementation. DbCommand's base class has no true async fallback — an unoverridden
    // ExecuteNonQueryAsync/ExecuteScalarAsync/ExecuteDbDataReaderAsync runs the *synchronous*
    // method on a thread-pool thread (Task.Factory.StartNew), silently discarding the provider's
    // real async I/O (e.g. SqlCommand/NpgsqlCommand/SqliteCommand). This is exactly the trap
    // BeginDbTransactionAsync above was already written to avoid; the same pattern must apply here.
    [Fact]
    public async Task ExecuteNonQueryAsync_DelegatesToInnerAsync()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockCmd = new Mock<DbCommand>();
        mockCmd.Setup(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(mockCmd.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        using var cmd = conn.CreateCommand();

        var result = await cmd.ExecuteNonQueryAsync();

        Assert.Equal(1, result);
        mockCmd.Verify(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockCmd.Verify(c => c.ExecuteNonQuery(), Times.Never);
    }

    [Fact]
    public async Task ExecuteScalarAsync_DelegatesToInnerAsync()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockCmd = new Mock<DbCommand>();
        mockCmd.Setup(c => c.ExecuteScalarAsync(It.IsAny<CancellationToken>())).ReturnsAsync("value");
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(mockCmd.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        using var cmd = conn.CreateCommand();

        var result = await cmd.ExecuteScalarAsync();

        Assert.Equal("value", result);
        mockCmd.Verify(c => c.ExecuteScalarAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockCmd.Verify(c => c.ExecuteScalar(), Times.Never);
    }

    [Fact]
    public async Task ExecuteReaderAsync_DelegatesToInnerAsync()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockCmd = new Mock<DbCommand>();
        var mockReader = new Mock<DbDataReader>();
        mockCmd.Protected()
            .Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync", ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockReader.Object);
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(mockCmd.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        using var cmd = conn.CreateCommand();

        var reader = await cmd.ExecuteReaderAsync();

        Assert.Same(mockReader.Object, reader);
        mockCmd.Protected().Verify("ExecuteDbDataReaderAsync", Times.Once(),
            ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>());
        mockCmd.Protected().Verify("ExecuteDbDataReader", Times.Never(), ItExpr.IsAny<CommandBehavior>());
    }

    // Minor: PermitCommand/PermitTransaction lacked the double-dispose guard PermitConnection
    // already has. Component.Dispose() (the base every DbCommand/DbTransaction ultimately
    // derives from) does not itself guard against re-entry, so calling Dispose() twice on the
    // same wrapper called Inner.Dispose() twice.
    [Fact]
    public async Task CreatedCommand_Dispose_OnlyDisposesInnerOnce()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockCmd = new Mock<DbCommand>();
        var disposeCount = 0;
        mockCmd.Protected()
            .Setup("Dispose", ItExpr.IsAny<bool>())
            .Callback<bool>(_ => disposeCount++);
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(mockCmd.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        var cmd = conn.CreateCommand();

        cmd.Dispose();
        cmd.Dispose();

        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task CreatedCommand_DisposeAsync_UsesInnerAsyncDisposalOnlyOnce()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockCmd = new Mock<DbCommand>();
        var disposeAsyncCount = 0;
        mockCmd.Setup(c => c.DisposeAsync())
            .Callback(() => disposeAsyncCount++)
            .Returns(ValueTask.CompletedTask);
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(mockCmd.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        var cmd = conn.CreateCommand();

        await cmd.DisposeAsync();
        await cmd.DisposeAsync();

        Assert.Equal(1, disposeAsyncCount);
    }

    [Fact]
    public async Task CreatedCommand_MapsItsTransactionToTheInnerTransaction()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var innerTransaction = new Mock<DbTransaction>();
        var innerCommand = new Mock<DbCommand>();
        _mockInner.Protected()
            .Setup<DbTransaction>("BeginDbTransaction", IsolationLevel.Unspecified)
            .Returns(innerTransaction.Object);
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(innerCommand.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();

        cmd.Transaction = tx;

        innerCommand.VerifySet(c => c.Transaction = innerTransaction.Object, Times.Once);
        Assert.Same(tx, cmd.Transaction);
    }

    [Fact]
    public async Task CreatedCommand_RejectsConnectionOrTransactionFromAnotherSource()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(new Mock<DbCommand>().Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var cmd = conn.CreateCommand();

        Assert.Throws<InvalidOperationException>(() => cmd.Connection = new Mock<DbConnection>().Object);
        Assert.Throws<InvalidOperationException>(() => cmd.Transaction = new Mock<DbTransaction>().Object);
    }

    [Fact]
    public async Task CreatedCommand_ForwardsSynchronousMembersAndConfiguration()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var innerCommand = new Mock<DbCommand>();
        var parameter = new Mock<DbParameter>().Object;
        var parameters = new Mock<DbParameterCollection>().Object;
        var reader = new Mock<DbDataReader>().Object;
        innerCommand.SetupGet(c => c.CommandText).Returns("select 1");
        innerCommand.SetupGet(c => c.CommandTimeout).Returns(30);
        innerCommand.SetupGet(c => c.CommandType).Returns(CommandType.Text);
        innerCommand.SetupGet(c => c.DesignTimeVisible).Returns(false);
        innerCommand.SetupGet(c => c.UpdatedRowSource).Returns(UpdateRowSource.None);
        innerCommand.Protected().SetupGet<DbParameterCollection>("DbParameterCollection").Returns(parameters);
        innerCommand.Protected().Setup<DbParameter>("CreateDbParameter").Returns(parameter);
        innerCommand.Setup(c => c.ExecuteNonQuery()).Returns(4);
        innerCommand.Setup(c => c.ExecuteScalar()).Returns("answer");
        innerCommand.Protected().Setup<DbDataReader>("ExecuteDbDataReader", CommandBehavior.SchemaOnly).Returns(reader);
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(innerCommand.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var cmd = conn.CreateCommand();

        Assert.Equal("select 1", cmd.CommandText);
        cmd.CommandText = "select 2";
        Assert.Equal(30, cmd.CommandTimeout);
        cmd.CommandTimeout = 31;
        Assert.Equal(CommandType.Text, cmd.CommandType);
        cmd.CommandType = CommandType.StoredProcedure;
        Assert.False(cmd.DesignTimeVisible);
        cmd.DesignTimeVisible = true;
        Assert.Equal(UpdateRowSource.None, cmd.UpdatedRowSource);
        cmd.UpdatedRowSource = UpdateRowSource.OutputParameters;
        Assert.Same(conn, cmd.Connection);
        cmd.Connection = conn;
        Assert.Same(parameters, cmd.Parameters);
        Assert.Same(parameter, cmd.CreateParameter());
        cmd.Cancel();
        cmd.Prepare();
        Assert.Equal(4, cmd.ExecuteNonQuery());
        Assert.Equal("answer", cmd.ExecuteScalar());
        Assert.Same(reader, cmd.ExecuteReader(CommandBehavior.SchemaOnly));

        innerCommand.VerifySet(c => c.CommandText = "select 2", Times.Once);
        innerCommand.VerifySet(c => c.CommandTimeout = 31, Times.Once);
        innerCommand.VerifySet(c => c.CommandType = CommandType.StoredProcedure, Times.Once);
        innerCommand.VerifySet(c => c.DesignTimeVisible = true, Times.Once);
        innerCommand.VerifySet(c => c.UpdatedRowSource = UpdateRowSource.OutputParameters, Times.Once);
        innerCommand.Verify(c => c.Cancel(), Times.Once);
        innerCommand.Verify(c => c.Prepare(), Times.Once);
    }

    [Fact]
    public async Task CreatedTransaction_ForwardsSynchronousAndAsynchronousMembers()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var innerTransaction = new Mock<DbTransaction>();
        innerTransaction.SetupGet(t => t.IsolationLevel).Returns(IsolationLevel.Serializable);
        _mockInner.Protected()
            .Setup<DbTransaction>("BeginDbTransaction", IsolationLevel.Unspecified)
            .Returns(innerTransaction.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var tx = conn.BeginTransaction();

        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
        Assert.Same(conn, tx.Connection);
        tx.Commit();
        tx.Rollback();
        await tx.CommitAsync();
        await tx.RollbackAsync();

        innerTransaction.Verify(t => t.Commit(), Times.Once);
        innerTransaction.Verify(t => t.Rollback(), Times.Once);
        innerTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        innerTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatedTransaction_Dispose_OnlyDisposesInnerOnce()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockTx = new Mock<DbTransaction>();
        var disposeCount = 0;
        mockTx.Protected()
            .Setup("Dispose", ItExpr.IsAny<bool>())
            .Callback<bool>(_ => disposeCount++);
        _mockInner.Protected()
            .Setup<DbTransaction>("BeginDbTransaction", IsolationLevel.Unspecified)
            .Returns(mockTx.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        var tx = conn.BeginTransaction();

        tx.Dispose();
        tx.Dispose();

        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task CreatedTransaction_DisposeAsync_OnlyDisposesInnerOnce()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var mockTx = new Mock<DbTransaction>();
        var disposeCount = 0;
        mockTx.Setup(t => t.DisposeAsync())
            .Callback(() => disposeCount++)
            .Returns(ValueTask.CompletedTask);
        _mockInner.Protected()
            .Setup<DbTransaction>("BeginDbTransaction", IsolationLevel.Unspecified)
            .Returns(mockTx.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        using var conn = await gate.OpenAsync();
        var tx = conn.BeginTransaction();

        await tx.DisposeAsync();
        await tx.DisposeAsync();

        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task CreateCommand_ForwardsProviderNativeAsyncOperations()
    {
        _mockInner.SetupGet(c => c.State).Returns(ConnectionState.Open);
        var innerCommand = new Mock<DbCommand>();
        var reader = new Mock<DbDataReader>();
        _mockInner.Protected().Setup<DbCommand>("CreateDbCommand").Returns(innerCommand.Object);
        innerCommand.Setup(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        innerCommand.Setup(c => c.ExecuteScalarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        innerCommand.Setup(c => c.PrepareAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerCommand.Protected()
            .Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync", CommandBehavior.Default, ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(reader.Object);

        using var gate = new StormGate(_mockDataSource.Object, 1, _timeout);
        await using var connection = await gate.OpenAsync();
        await using var command = connection.CreateCommand();

        Assert.Equal(3, await command.ExecuteNonQueryAsync());
        Assert.Equal(7, await command.ExecuteScalarAsync());
        await command.PrepareAsync();
        Assert.Same(reader.Object, await command.ExecuteReaderAsync());

        innerCommand.Verify(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()), Times.Once);
        innerCommand.Verify(c => c.ExecuteScalarAsync(It.IsAny<CancellationToken>()), Times.Once);
        innerCommand.Verify(c => c.PrepareAsync(It.IsAny<CancellationToken>()), Times.Once);
        innerCommand.Protected().Verify("ExecuteDbDataReaderAsync", Times.Once(), CommandBehavior.Default,
            ItExpr.IsAny<CancellationToken>());
    }
}
