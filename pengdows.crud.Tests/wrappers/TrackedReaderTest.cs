#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.connection;
using pengdows.crud.fakeDb;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests.wrappers;

public class TrackedReaderTests
{
    private class TestTrackedConnection : ITrackedConnection
    {
        public int CloseCallCount { get; private set; }
        public bool WasClosed => CloseCallCount > 0;

        private string _connectionString = "test";

        [AllowNull]
        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public int ConnectionTimeout => 30;
        public string Database => "testdb";
        public string DataSource => "localhost";
        public string ServerVersion => "1.0.0";
        public ConnectionState State => ConnectionState.Open;
        public ConnectionLocalState LocalState { get; } = new();

        public void Close()
        {
            CloseCallCount++;
        }

        public void Dispose()
        {
            Close();
        }

        public ValueTask DisposeAsync()
        {
            Close();
            return ValueTask.CompletedTask;
        }

        public void Open()
        {
        }

        public Task OpenAsync()
        {
            return Task.CompletedTask;
        }

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public IDbTransaction BeginTransaction()
        {
            throw new NotImplementedException();
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            throw new NotImplementedException();
        }

        public void ChangeDatabase(string databaseName)
        {
            throw new NotImplementedException();
        }

        public IDbCommand CreateCommand()
        {
            throw new NotImplementedException();
        }

        public DataTable GetSchema()
        {
            return new DataTable();
        }

        public DataTable GetSchema(string dataSourceInformation)
        {
            return new DataTable();
        }

        public DataTable GetSchema(string collectionName, string?[]? restrictionValues)
        {
            return new DataTable();
        }

        public ILockerAsync GetLock()
        {
            return new TestLockerAsync();
        }
    }

    private class TestAsyncDisposable : IAsyncDisposable
    {
        public int DisposeCallCount { get; private set; }
        public bool WasDisposed => DisposeCallCount > 0;

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private class TestLockerAsync : ILockerAsync
    {
        public int DisposeCallCount { get; private set; }
        public bool WasDisposed => DisposeCallCount > 0;

        public void Lock()
        {
            // No-op for test
        }

        public Task LockAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingSynchronizationContext : SynchronizationContext
    {
    }

    private sealed class ContextSensitiveLocker : ILockerAsync
    {
        private readonly SynchronizationContext _forbidden;

        private readonly TaskCompletionSource<bool> _observedForbidden =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ContextSensitiveLocker(SynchronizationContext forbidden)
        {
            _forbidden = forbidden;
        }

        public bool ObservedForbiddenContext => _observedForbidden.Task.IsCompleted && _observedForbidden.Task.Result;

        public void Lock()
        {
            // No-op for test
        }

        public Task LockAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            // Sync dispose for test
        }

        public async ValueTask DisposeAsync()
        {
            var observedForbidden = SynchronizationContext.Current == _forbidden;
            _observedForbidden.TrySetResult(observedForbidden);

            // Simulate asynchronous disposal work that would capture the current context
            await Task.Yield();
        }
    }

    private sealed class TrackingFakeReader : fakeDbDataReader
    {
        public bool NextResultCalled { get; private set; }

        public TrackingFakeReader() : base(Array.Empty<Dictionary<string, object>>())
        {
        }

        public override bool NextResult()
        {
            NextResultCalled = true;
            return base.NextResult();
        }
    }

    [Fact]
    public async Task ReadAsync_ReturnsFalseAndDisposes_WhenDone()
    {
        // Create empty fakeDb reader (no rows, so ReadAsync returns false)
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());

        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, true);

        var result = await tracked.ReadAsync();

        Assert.False(result);
        Assert.True(locker.WasDisposed);
        Assert.True(connection.WasClosed);
    }

    [Fact]
    public void Read_ReturnsFalseAndDisposes_WhenDone()
    {
        // Create empty fakeDb reader (no rows, so Read returns false)
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());

        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, true);

        var result = tracked.Read();

        Assert.False(result);
        Assert.True(connection.WasClosed);
        Assert.True(locker.WasDisposed);
    }

    [Fact]
    public async Task DisposeAsync_OnlyOnce()
    {
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());

        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, true);

        await tracked.DisposeAsync();
        await tracked.DisposeAsync();

        // Should only dispose locker once despite being called twice
        Assert.Equal(1, locker.DisposeCallCount);
        Assert.Equal(1, connection.CloseCallCount);
    }

    [Fact]
    public void Dispose_OnlyOnce()
    {
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());

        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, true);

        tracked.Dispose();
        tracked.Dispose();

        // Should only dispose locker once despite being called twice
        Assert.Equal(1, locker.DisposeCallCount);
        Assert.Equal(1, connection.CloseCallCount);
    }

    [Fact]
    public void Accessors_ForwardToReader()
    {
        var row = new Dictionary<string, object>
        {
            ["col"] = "value2",
            ["field0"] = "value"
        };

        using var reader = new fakeDbDataReader(new[] { row });
        reader.Read(); // Position at first row

        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, false);

        Assert.Equal(2, tracked.FieldCount);
        Assert.Equal("value2", tracked[0]); // First column "col" has "value2"
        Assert.Equal("value", tracked[1]); // Second column "field0" has "value"  
        Assert.Equal("value2", tracked["col"]);
        Assert.Equal("value", tracked["field0"]);
    }

    [Fact]
    public void Read_DoesNotClose_WhenShouldCloseConnectionFalse()
    {
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());
        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, false);

        var result = tracked.Read();

        Assert.False(result);
        Assert.False(connection.WasClosed);
        Assert.True(locker.WasDisposed);
    }

    [Fact]
    public async Task ReadAsync_DisposesAfterLastRow()
    {
        var rows = new[]
        {
            new Dictionary<string, object> { ["value"] = 1 }
        };

        using var reader = new fakeDbDataReader(rows);
        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, true);

        var first = await tracked.ReadAsync();
        Assert.True(first);
        Assert.False(connection.WasClosed);
        Assert.Equal(0, locker.DisposeCallCount);

        var second = await tracked.ReadAsync();
        Assert.False(second);
        Assert.True(connection.WasClosed);
        Assert.True(locker.WasDisposed);
    }

    [Fact]
    public void NextResult_ThrowsNotSupported_AndDoesNotCallUnderlying()
    {
        var reader = new TrackingFakeReader();
        var tracked = new TrackedReader(reader, new TestTrackedConnection(), new TestLockerAsync(), false);

        Assert.Throws<NotSupportedException>(() => tracked.NextResult());
        Assert.False(reader.NextResultCalled);
    }

    [Fact]
    public async Task DisposeAsync_ClosesConnection_WhenShouldCloseTrue()
    {
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());
        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, true);

        await tracked.DisposeAsync();

        Assert.True(connection.WasClosed);
        Assert.True(locker.WasDisposed);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotCloseConnection_WhenShouldCloseFalse()
    {
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());
        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, false);

        await tracked.DisposeAsync();

        Assert.False(connection.WasClosed);
        Assert.True(locker.WasDisposed);
    }

    [Fact]
    public void Dispose_DoesNotCloseConnection_WhenShouldCloseFalse()
    {
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());
        var connection = new TestTrackedConnection();
        var locker = new TestLockerAsync();

        var tracked = new TrackedReader(reader, connection, locker, false);

        tracked.Dispose();

        Assert.False(connection.WasClosed);
        Assert.True(locker.WasDisposed);
    }

    [Fact]
    public void WrapperMethods_DelegateToUnderlyingReader()
    {
        var row = new Dictionary<string, object>
        {
            ["Bool"] = true,
            ["Byte"] = (byte)1,
            ["String"] = "text",
            ["Decimal"] = 1.2m,
            ["Double"] = 2.3,
            ["Float"] = 3.4f,
            ["Short"] = (short)5,
            ["Int"] = 6,
            ["Long"] = 7L,
            ["Guid"] = Guid.NewGuid(),
            ["Char"] = 'x',
            ["Date"] = new DateTime(2025, 1, 1)
        };

        using var reader = new fakeDbDataReader(new[] { row });
        reader.Read();

        var tracked = new TrackedReader(reader, new TestTrackedConnection(), new TestLockerAsync(), false);

        Assert.True(tracked.GetBoolean(0));
        Assert.Equal((byte)1, tracked.GetByte(1));
        Assert.Equal("text", tracked.GetString(2));
        Assert.Equal(1.2m, tracked.GetDecimal(3));
        Assert.Equal(2.3, tracked.GetDouble(4));
        Assert.Equal(3.4f, tracked.GetFloat(5));
        Assert.Equal((short)5, tracked.GetInt16(6));
        Assert.Equal(6, tracked.GetInt32(7));
        Assert.Equal(7L, tracked.GetInt64(8));
        Assert.Equal(row["Guid"], tracked.GetGuid(9));
        Assert.Equal('x', tracked.GetChar(10));
        Assert.Equal(new DateTime(2025, 1, 1), tracked.GetDateTime(11));
        Assert.Equal("Bool", tracked.GetName(0));
        Assert.Equal(2, tracked.GetOrdinal("String"));
        Assert.False(tracked.IsDBNull(0));
        Assert.Equal(row["String"], tracked["String"]);
        Assert.Equal(row["Int"], tracked[7]);
        Assert.Null(tracked.GetSchemaTable());
        Assert.Equal(0, tracked.Depth);
        Assert.False(tracked.IsClosed);
        Assert.Equal(0, tracked.RecordsAffected);
    }

    [Fact]
    public void Dispose_DoesNotInvokeLockerOnCurrentSynchronizationContext()
    {
        using var reader = new fakeDbDataReader(Array.Empty<Dictionary<string, object>>());
        var connection = new TestTrackedConnection();
        var blockingContext = new BlockingSynchronizationContext();
        var locker = new ContextSensitiveLocker(blockingContext);

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(blockingContext);
        try
        {
            var tracked = new TrackedReader(reader, connection, locker, false);

            tracked.Dispose();

            Assert.False(locker.ObservedForbiddenContext);
            Assert.False(connection.WasClosed);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}