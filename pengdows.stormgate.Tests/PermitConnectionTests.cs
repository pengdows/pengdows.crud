using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace pengdows.stormgate.Tests;

public class PermitConnectionTests
{
    private const string ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";

    private readonly fakeDbFactory _factory = new(pengdows.crud.enums.SupportedDatabase.Sqlite);
    private readonly fakeDbConnection _inner;
    private readonly FakeDbDataSource _dataSource;
    private readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(50);

    public PermitConnectionTests()
    {
        // The first connection the data source hands out is the one the tests look at.
        _inner = new fakeDbConnection { EmulatedProduct = pengdows.crud.enums.SupportedDatabase.Sqlite };
        _factory.Connections.Add(_inner);
        _dataSource = new FakeDbDataSource(ConnectionString, _factory);
    }

    private fakeDbCommand InnerCommand => _inner.LastCreatedCommand!;

    private static fakeDbDataReader OneRowReader() =>
        new(new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>
        {
            new() { ["Value"] = 1 }
        });

    [Fact]
    public async Task Delegation_Works()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();

        // Reads come from the inner connection ...
        Assert.Equal(_inner.ConnectionString, conn.ConnectionString);
        Assert.Equal(_inner.Database, conn.Database);
        Assert.Equal(_inner.DataSource, conn.DataSource);
        Assert.Equal(_inner.ServerVersion, conn.ServerVersion);
        Assert.Equal(ConnectionState.Open, conn.State);

        // ... and writes and calls reach it.
        conn.ChangeDatabase("new_db");
        Assert.Contains("ChangeDatabase(new_db)", _inner.Operations);
        Assert.Equal("new_db", conn.Database);

        conn.BeginTransaction(IsolationLevel.ReadCommitted);
        Assert.Single(_inner.Operations, "BeginTransaction(ReadCommitted)");

        conn.CreateCommand();
        Assert.Single(_inner.Operations, "CreateCommand");

        // A connection string changes only while closed: an open connection refuses one, as every provider does.
        conn.Close();
        conn.ConnectionString = "Data Source=new_cs;EmulatedProduct=Sqlite";
        Assert.Equal(_inner.ConnectionString, conn.ConnectionString);
        Assert.Contains("new_cs", _inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloseAsync_CallsInnerAndReleasesPermit()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        await conn.CloseAsync();

        Assert.Single(_inner.Operations, "CloseAsync");

        // Should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task GuardMethods_ThrowWhenClosed()
    {
        // Arrange
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();
        _inner.Close();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => conn.BeginTransaction());
        Assert.Throws<InvalidOperationException>(() => conn.CreateCommand());
    }

    // P2: Open/OpenAsync should be a no-op when already open (Dapper/EF Core call Open defensively)
    [Fact]
    public async Task Open_WhenAlreadyOpen_IsNoOp()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();

        var ex = Record.Exception(() => conn.Open());
        Assert.Null(ex);

        var exAsync = await Record.ExceptionAsync(() => conn.OpenAsync());
        Assert.Null(exAsync);
    }

    // P2: Open/OpenAsync when connection is not open should throw NotSupportedException
    [Fact]
    public async Task Open_WhenNotOpen_ThrowsNotSupportedException()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();
        _inner.Close();

        Assert.Throws<NotSupportedException>(() => conn.Open());
        await Assert.ThrowsAsync<NotSupportedException>(() => conn.OpenAsync());
    }

    [Fact]
    public async Task Close_CallsInnerAndReleasesPermit()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        conn.Close();

        Assert.Single(_inner.Operations, "Close");

        // Should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task Dispose_CallsInnerAndReleasesPermit()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        conn.Dispose();

        Assert.Single(_inner.Operations, "Dispose");

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
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        conn.Dispose();

        var callOrder = _inner.Operations.ToList();
        Assert.Contains("Close", callOrder);
        Assert.Contains("Dispose", callOrder);
        Assert.True(
            callOrder.IndexOf("Close") < callOrder.IndexOf("Dispose"),
            $"Expected Close before Dispose but got: [{string.Join(", ", callOrder)}]");
    }

    [Fact]
    public async Task DisposeAsync_CallsInnerAndReleasesPermit()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        await conn.DisposeAsync();

        Assert.Single(_inner.Operations, "DisposeAsync");

        // Should be able to open again
        var conn2 = await gate.OpenAsync();
        Assert.NotNull(conn2);
    }

    [Fact]
    public async Task DisposeAsync_ClosesInnerBeforeDisposingIt_WhenConnectionIsOpen()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        await conn.DisposeAsync();

        var callOrder = _inner.Operations.ToList();
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
        using var gate = new StormGate(_dataSource, 1, _timeout);
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
        _inner.SetFailOnClose(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Close throws, but the finally block in Close() still releases the permit (_released = 1)
        Assert.Throws<InvalidOperationException>(() => conn.Close());

        // _released is now 1, inner.State is still Open — guard must fire
        Assert.Throws<InvalidOperationException>(() => conn.CreateCommand());
    }

    [Fact]
    public async Task BeginTransaction_ThrowsAfterPermitReleased_EvenIfInnerStateIsStillOpen()
    {
        _inner.SetFailOnClose(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_dataSource, 1, _timeout);
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
        _inner.SetFailOnClose(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Exception from Close() may propagate, but inner.Dispose must still run.
        _ = Record.Exception(() => conn.Dispose());

        Assert.Contains("Dispose", _inner.Operations);
    }

    // P1-1: DisposeAsync must call _inner.DisposeAsync() even when CloseAsync() throws.
    // The permit must also be released.
    [Fact]
    public async Task DisposeAsync_StillDisposesInner_WhenCloseAsyncThrows()
    {
        _inner.SetFailOnClose(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();

        // Exception from CloseAsync() may propagate, but inner.DisposeAsync must still run.
        _ = await Record.ExceptionAsync(async () => await conn.DisposeAsync());

        Assert.Contains("DisposeAsync", _inner.Operations);
    }

    // P1-1: DisposeAsync permit must be released even when CloseAsync() throws.
    [Fact]
    public async Task DisposeAsync_ReleasesPermit_WhenCloseAsyncThrows()
    {
        _inner.SetFailOnClose(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_dataSource, 1, _timeout);
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
        _inner.SetFailOnClose(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_dataSource, 1, _timeout);
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
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();
        conn.Dispose();

        Assert.Throws<ObjectDisposedException>(() => conn.Open());
    }

    [Fact]
    public async Task OpenAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();
        conn.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => conn.OpenAsync());
    }

    [Fact]
    public async Task CreateCommand_AfterDispose_ThrowsObjectDisposedException()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        var conn = await gate.OpenAsync();
        conn.Dispose();

        Assert.Throws<ObjectDisposedException>(() => conn.CreateCommand());
    }

    [Fact]
    public async Task BeginTransaction_AfterDispose_ThrowsObjectDisposedException()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
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
        _inner.SetFailOnClose(new InvalidOperationException("provider close failed"));

        using var gate = new StormGate(_dataSource, 1, _timeout);
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
        Assert.DoesNotContain("Close", _inner.Operations);
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
        using var gate = new StormGate(_dataSource, 1, _timeout);
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

    [Fact]
    public async Task AbandonedConnection_IsCollectedAndReleasesPermit()
    {
        using var gate = new StormGate(new CollectibleDataSource(), 1, _timeout);
        var weakConnection = await AbandonConnectionAsync(gate);

        for (var attempt = 0; attempt < 10 && weakConnection.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Yield();
        }

        Assert.False(weakConnection.IsAlive, "The abandoned gated connection should be collectible.");
        var replacement = await gate.OpenAsync();
        Assert.NotNull(replacement);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> AbandonConnectionAsync(StormGate gate)
    {
        var connection = await gate.OpenAsync();
        return new WeakReference(connection);
    }

    private sealed class CollectibleDataSource : DbDataSource
    {
        public override string ConnectionString => "collectible";
        protected override DbConnection CreateDbConnection() => new CollectibleConnection();
    }

    private sealed class CollectibleConnection : DbConnection
    {
        private ConnectionState _state;
        [AllowNull]
        public override string ConnectionString { get; set; } = "collectible";
        public override string Database => "collectible";
        public override string DataSource => "collectible";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName)
        {
        }
        public override void Open()
        {
            var original = _state;
            _state = ConnectionState.Open;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }
        public override void Close()
        {
            var original = _state;
            _state = ConnectionState.Closed;
            OnStateChange(new StateChangeEventArgs(original, _state));
        }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    // Minor: BeginDbTransactionAsync must override to call the inner's async path, not fall
    // back to the sync BeginDbTransaction default in DbConnection.
    [Fact]
    public async Task BeginTransactionAsync_DelegatesToInnerAsync()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();

        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        Assert.NotSame(_inner.LastTransaction, tx);
        Assert.Same(conn, tx.Connection);
        Assert.Contains("BeginTransactionAsync(ReadCommitted)", _inner.Operations);
        Assert.DoesNotContain("BeginTransaction(ReadCommitted)", _inner.Operations);
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
        _inner.EnqueueNonQueryResult(1);

        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();
        using var cmd = conn.CreateCommand();

        var result = await cmd.ExecuteNonQueryAsync();

        Assert.Equal(1, result);
        Assert.Single(InnerCommand.Operations, "ExecuteNonQueryAsync");
        Assert.DoesNotContain("ExecuteNonQuery", InnerCommand.Operations);
    }

    [Fact]
    public async Task ExecuteScalarAsync_DelegatesToInnerAsync()
    {
        _inner.EnqueueScalarResult("value");

        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();
        using var cmd = conn.CreateCommand();

        var result = await cmd.ExecuteScalarAsync();

        Assert.Equal("value", result);
        Assert.Single(InnerCommand.Operations, "ExecuteScalarAsync");
        Assert.DoesNotContain("ExecuteScalar", InnerCommand.Operations);
    }

    [Fact]
    public async Task ExecuteReaderAsync_DelegatesToInnerAsync()
    {
        var innerReader = OneRowReader();
        _inner.EnqueueReaderResult(innerReader);

        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();
        using var cmd = conn.CreateCommand();

        var reader = await cmd.ExecuteReaderAsync();

        Assert.Same(innerReader, reader);
        Assert.Single(InnerCommand.Operations, "ExecuteReaderAsync(Default)");
        Assert.DoesNotContain(InnerCommand.Operations, o => o.StartsWith("ExecuteReader(", StringComparison.Ordinal));
    }

    // Minor: PermitCommand/PermitTransaction lacked the double-dispose guard PermitConnection
    // already has. Component.Dispose() (the base every DbCommand/DbTransaction ultimately
    // derives from) does not itself guard against re-entry, so calling Dispose() twice on the
    // same wrapper called Inner.Dispose() twice.
    [Fact]
    public async Task CreatedCommand_Dispose_OnlyDisposesInnerOnce()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();
        var cmd = conn.CreateCommand();

        cmd.Dispose();
        cmd.Dispose();

        Assert.Equal(1, InnerCommand.Operations.Count(o => o == "Dispose"));
    }

    [Fact]
    public async Task CreatedCommand_DisposeAsync_UsesInnerAsyncDisposalOnlyOnce()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        var cmd = conn.CreateCommand();

        await cmd.DisposeAsync();
        await cmd.DisposeAsync();

        Assert.Equal(1, InnerCommand.Operations.Count(o => o == "DisposeAsync"));
    }

    [Fact]
    public async Task CreatedCommand_MapsItsTransactionToTheInnerTransaction()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();

        cmd.Transaction = tx;

        Assert.Same(_inner.LastTransaction, InnerCommand.Transaction);
        Assert.Same(tx, cmd.Transaction);
    }

    [Fact]
    public async Task CreatedCommand_RejectsConnectionOrTransactionFromAnotherSource()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var foreign = new fakeDbConnection();

        Assert.Throws<InvalidOperationException>(() => cmd.Connection = foreign);
        Assert.Throws<InvalidOperationException>(() => cmd.Transaction = new fakeDbTransaction(foreign, IsolationLevel.Unspecified));
    }

    [Fact]
    public async Task CreatedCommand_ForwardsSynchronousMembersAndConfiguration()
    {
        var innerReader = OneRowReader();
        _inner.EnqueueNonQueryResult(4);
        _inner.EnqueueScalarResult("answer");
        _inner.EnqueueReaderResult(innerReader);

        using var gate = new StormGate(_dataSource, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var inner = InnerCommand;
        inner.CommandText = "select 1";
        inner.CommandTimeout = 30;
        inner.CommandType = CommandType.Text;
        inner.DesignTimeVisible = false;
        inner.UpdatedRowSource = UpdateRowSource.None;

        // Reads come from the inner command, writes reach it.
        Assert.Equal("select 1", cmd.CommandText);
        cmd.CommandText = "select 2";
        Assert.Equal("select 2", inner.CommandText);
        Assert.Equal(30, cmd.CommandTimeout);
        cmd.CommandTimeout = 31;
        Assert.Equal(31, inner.CommandTimeout);
        Assert.Equal(CommandType.Text, cmd.CommandType);
        cmd.CommandType = CommandType.StoredProcedure;
        Assert.Equal(CommandType.StoredProcedure, inner.CommandType);
        Assert.False(cmd.DesignTimeVisible);
        cmd.DesignTimeVisible = true;
        Assert.True(inner.DesignTimeVisible);
        Assert.Equal(UpdateRowSource.None, cmd.UpdatedRowSource);
        cmd.UpdatedRowSource = UpdateRowSource.OutputParameters;
        Assert.Equal(UpdateRowSource.OutputParameters, inner.UpdatedRowSource);
        Assert.Same(conn, cmd.Connection);
        cmd.Connection = conn;
        Assert.Same(inner.Parameters, cmd.Parameters);
        Assert.IsAssignableFrom<DbParameter>(cmd.CreateParameter());
        cmd.Cancel();
        cmd.Prepare();
        Assert.Equal(4, cmd.ExecuteNonQuery());
        Assert.Equal("answer", cmd.ExecuteScalar());
        Assert.Same(innerReader, cmd.ExecuteReader(CommandBehavior.SchemaOnly));

        Assert.Equal(
            new[] { "CreateParameter", "Cancel", "Prepare", "ExecuteNonQuery", "ExecuteScalar", "ExecuteReader(SchemaOnly)" },
            inner.Operations.ToArray());
    }

    [Fact]
    public async Task CreatedTransaction_ForwardsSynchronousAndAsynchronousMembers()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        await using var conn = await gate.OpenAsync();
        await using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
        Assert.Same(conn, tx.Connection);
        tx.Commit();
        // A completed transaction refuses another completion; each attempt still reaches the inner transaction.
        Assert.Throws<InvalidOperationException>(() => tx.Rollback());
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.RollbackAsync());

        Assert.Equal(new[] { "Commit", "Rollback", "CommitAsync", "RollbackAsync" }, _inner.LastTransaction!.Operations.ToArray());
    }

    [Fact]
    public async Task CreatedTransaction_Dispose_OnlyDisposesInnerOnce()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();
        var tx = conn.BeginTransaction();

        tx.Dispose();
        tx.Dispose();

        Assert.Equal(1, _inner.LastTransaction!.Operations.Count(o => o == "Dispose"));
    }

    [Fact]
    public async Task CreatedTransaction_DisposeAsync_OnlyDisposesInnerOnce()
    {
        using var gate = new StormGate(_dataSource, 1, _timeout);
        using var conn = await gate.OpenAsync();
        var tx = conn.BeginTransaction();

        await tx.DisposeAsync();
        await tx.DisposeAsync();

        Assert.Equal(1, _inner.LastTransaction!.Operations.Count(o => o == "DisposeAsync"));
    }

    [Fact]
    public async Task CreateCommand_ForwardsProviderNativeAsyncOperations()
    {
        var innerReader = OneRowReader();
        _inner.EnqueueNonQueryResult(3);
        _inner.EnqueueScalarResult(7);
        _inner.EnqueueReaderResult(innerReader);

        using var gate = new StormGate(_dataSource, 1, _timeout);
        await using var connection = await gate.OpenAsync();
        await using var command = connection.CreateCommand();

        Assert.Equal(3, await command.ExecuteNonQueryAsync());
        Assert.Equal(7, await command.ExecuteScalarAsync());
        await command.PrepareAsync();
        Assert.Same(innerReader, await command.ExecuteReaderAsync());

        Assert.Equal(
            new[] { "ExecuteNonQueryAsync", "ExecuteScalarAsync", "PrepareAsync", "ExecuteReaderAsync(Default)" },
            InnerCommand.Operations.ToArray());
    }
}
