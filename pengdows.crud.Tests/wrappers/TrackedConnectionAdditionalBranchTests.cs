using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers;

public class TrackedConnectionAdditionalBranchTests
{
    [Fact]
    public void Close_WhenAlreadyClosed_NoOp()
    {
        using var conn = new fakeDbConnection();
        using var tracked = new TrackedConnection(conn);

        tracked.Close();

        Assert.Equal(ConnectionState.Closed, conn.State);
    }

    [Fact]
    public void HandleMetricsStateChange_WithNullCollector_DoesNotThrow()
    {
        using var conn = new fakeDbConnection();
        using var tracked = new TrackedConnection(conn);
        var method = typeof(TrackedConnection).GetMethod("HandleMetricsStateChange", BindingFlags.Instance | BindingFlags.NonPublic);

        method!.Invoke(tracked, new object?[] { null, new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open) });
    }

    [Fact]
    public async Task DisposeAsync_WhenUnderlyingDisposeAsyncThrows_FallsBackToSync()
    {
        using var conn = new ThrowingDisposeAsyncConnection();
        await using var tracked = new TrackedConnection(conn);

        await tracked.OpenAsync();
        await tracked.DisposeAsync();

        Assert.True(conn.DisposeAsyncCalled);
        Assert.True(conn.DisposeCalled);
    }

    [Fact]
    public async Task DisposeAsync_SharedConnection_TimesOutAndRetries()
    {
        await using var conn = new fakeDbConnection();
        await using var tracked = new TrackedConnection(conn, isSharedConnection: true);

        await tracked.OpenAsync();

        await using var locker = tracked.GetLock();
        await locker.LockAsync();

        var disposeTask = tracked.DisposeAsync().AsTask();
        await Task.Delay(5500);
        await locker.DisposeAsync();

        await disposeTask;
        Assert.Equal(ConnectionState.Closed, conn.State);
    }

    private sealed class ThrowingDisposeAsyncConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public bool DisposeAsyncCalled { get; private set; }
        public bool DisposeCalled { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;
        public override int ConnectionTimeout => 0;

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void ChangeDatabase(string databaseName)
        {
        }

        protected override DbCommand CreateDbCommand()
        {
            return new fakeDbCommand(this);
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            return new StubTransaction(this, isolationLevel);
        }

        public override ValueTask DisposeAsync()
        {
            DisposeAsyncCalled = true;
            throw new InvalidOperationException("dispose async failed");
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCalled = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StubTransaction : DbTransaction
    {
        private readonly DbConnection _connection;
        private readonly IsolationLevel _isolationLevel;

        public StubTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection;
            _isolationLevel = isolationLevel;
        }

        protected override DbConnection DbConnection => _connection;
        public override IsolationLevel IsolationLevel => _isolationLevel;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }
}
