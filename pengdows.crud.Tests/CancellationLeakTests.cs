using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class CancellationLeakTests
{
    private class HangingFakeDbCommand : fakeDbCommand
    {
        private readonly TaskCompletionSource<bool> _tcs = new();

        public HangingFakeDbCommand(DbConnection connection) : base(connection) { }

        public override async Task<int> ExecuteNonQueryAsync(CancellationToken ct)
        {
            try
            {
                // Wait for the task to be completed or cancelled via the token
                await _tcs.Task.WaitAsync(ct);
                return 1;
            }
            catch (OperationCanceledException)
            {
                // This is expected when the token is cancelled
                throw;
            }
        }

        public void Release() => _tcs.TrySetResult(true);
    }

    private class HangingFakeDbDataReader : fakeDbDataReader
    {
        private TaskCompletionSource<bool> _tcs = new();

        public HangingFakeDbDataReader(IEnumerable<Dictionary<string, object>> rows) : base(rows) { }

        public override async Task<bool> ReadAsync(CancellationToken ct)
        {
            try
            {
                await _tcs.Task.WaitAsync(ct);
                // Reset TCS for next read if needed
                _tcs = new TaskCompletionSource<bool>();
                return base.Read();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        public void Release() => _tcs.TrySetResult(true);
    }

    private class HangingFakeDbConnection : fakeDbConnection
    {
        public HangingFakeDbCommand? LastHangingCommand { get; private set; }

        protected override DbCommand CreateDbCommand()
        {
            var cmd = new HangingFakeDbCommand(this);
            LastHangingCommand = cmd;
            return cmd;
        }
    }

    private class ReaderProxyConnection : fakeDbConnection
    {
        private readonly DbDataReader _reader;

        public ReaderProxyConnection(DbDataReader reader)
        {
            _reader = reader;
        }

        protected override DbCommand CreateDbCommand()
        {
            return new ReaderProxyCommand(this, _reader);
        }
    }

    private class ReaderProxyCommand : fakeDbCommand
    {
        private readonly DbDataReader _reader;

        public ReaderProxyCommand(DbConnection connection, DbDataReader reader) : base(connection)
        {
            _reader = reader;
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken ct)
        {
            return Task.FromResult(_reader);
        }
    }

    [Table("test")]
    private class TestEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WhenCanceled_DoesNotLeakPoolSlot()
    {
        // Setup: Use a context with MaxConcurrentWrites = 1 to easily detect leaks
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentWrites = 1,
            MaxConcurrentReads = 1,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new HangingFakeDbConnection();
        conn.EmulatedProduct = SupportedDatabase.PostgreSql;
        factory.Connections.Add(conn);

        var snapshotBefore = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(1, snapshotBefore.MaxSlots);
        Assert.Equal(0, snapshotBefore.InUse);

        using var sc = ctx.CreateSqlContainer("INSERT INTO test (id) VALUES (1)");
        using var cts = new CancellationTokenSource();

        var task = sc.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, cts.Token);
        await Task.Delay(200);

        var snapshotDuring = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(1, snapshotDuring.InUse);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);

        await Task.Delay(100);
        var snapshotAfter = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(0, snapshotAfter.InUse);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn2.EnqueueScalarResult(42);
        factory.Connections.Add(conn2);

        using var sc2 = ctx.CreateSqlContainer("SELECT 1");
        var result = await sc2.ExecuteScalarRequiredAsync<int>();
        Assert.Equal(42, result);

        var snapshotFinal = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(0, snapshotFinal.InUse);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WhenCanceledDuringRead_DoesNotLeakPoolSlot()
    {
        // Setup
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentReads = 1,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var rows = new[] { new Dictionary<string, object> { ["Id"] = 1 } };
        var hangingReader = new HangingFakeDbDataReader(rows);

        var connProxy = new ReaderProxyConnection(hangingReader);
        connProxy.EmulatedProduct = SupportedDatabase.PostgreSql;
        factory.Connections.Add(connProxy);

        using var sc = ctx.CreateSqlContainer("SELECT * FROM test");

        var reader = await sc.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, CancellationToken.None);

        var snapshotDuring = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(1, snapshotDuring.InUse);

        using var cts = new CancellationTokenSource();
        var readTask = reader.ReadAsync(cts.Token);

        await Task.Delay(200);
        Assert.False(readTask.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await readTask);

        await reader.DisposeAsync();

        await Task.Delay(100);
        var snapshotAfter = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(0, snapshotAfter.InUse);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn2.EnqueueScalarResult(42);
        factory.Connections.Add(conn2);
        using var sc2 = ctx.CreateSqlContainer("SELECT 1");
        var result = await sc2.ExecuteScalarRequiredAsync<int>();
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RetrieveStreamAsync_WhenCanceledDuringIteration_DoesNotLeakPoolSlot()
    {
        // Setup
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            MaxConcurrentReads = 1,
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var rows = new[]
        {
            new Dictionary<string, object> { ["id"] = 1 },
            new Dictionary<string, object> { ["id"] = 2 }
        };
        var hangingReader = new HangingFakeDbDataReader(rows);

        var connProxy = new ReaderProxyConnection(hangingReader);
        connProxy.EmulatedProduct = SupportedDatabase.PostgreSql;
        factory.Connections.Add(connProxy);

        var gateway = new TableGateway<TestEntity, int>(ctx, null);

        using var cts = new CancellationTokenSource();
        var stream = gateway.RetrieveStreamAsync(new[] { 1, 2 }, null, cts.Token);
        var enumerator = stream.GetAsyncEnumerator(cts.Token);

        // 1. First row
        var moveNext1 = enumerator.MoveNextAsync();
        hangingReader.Release();
        Assert.True(await moveNext1);
        Assert.Equal(1, enumerator.Current.Id);

        var snapshotDuring = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(1, snapshotDuring.InUse);

        // 2. Second row - this one will hang
        var moveNext2 = enumerator.MoveNextAsync();
        await Task.Delay(200);
        Assert.False(moveNext2.IsCompleted);

        // 3. Cancel
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await moveNext2);

        // 4. Dispose enumerator
        await enumerator.DisposeAsync();

        // 5. Verify the slot is released
        await Task.Delay(100);
        var snapshotAfter = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(0, snapshotAfter.InUse);

        // 6. Final verification
        var connFinal = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        connFinal.EnqueueScalarResult(42);
        factory.Connections.Add(connFinal);

        using var sc2 = ctx.CreateSqlContainer("SELECT 1");
        var result = await sc2.ExecuteScalarRequiredAsync<int>();
        Assert.Equal(42, result);
    }
}
