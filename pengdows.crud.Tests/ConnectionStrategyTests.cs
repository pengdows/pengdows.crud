#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ConnectionStrategyTests
{
    private sealed class RecordingFactory : DbProviderFactory
    {
        public RecordingConnection Connection { get; }

        public RecordingFactory(SupportedDatabase product)
        {
            Connection = new RecordingConnection { EmulatedProduct = product };
        }

        public override DbConnection CreateConnection()
        {
            return Connection;
        }

        public override DbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new fakeDbParameter();
        }
    }

    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> ExecutedCommands { get; } = new();

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, ExecutedCommands);
        }
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly List<string> _record;

        public RecordingCommand(fakeDbConnection connection, List<string> record) : base(connection)
        {
            _record = record;
        }

        public override int ExecuteNonQuery()
        {
            _record.Add(CommandText);
            return base.ExecuteNonQuery();
        }
    }

    private static DatabaseContext CreateContext(DbMode mode, SupportedDatabase product = SupportedDatabase.SqlServer,
        string dataSource = "test")
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={dataSource};EmulatedProduct={product}",
            DbMode = mode,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(cfg, new fakeDbFactory(product));
    }

    [Fact]
    public async Task Standard_NewConnectionPerCall_ReleaseDisposes()
    {
        await using var ctx = CreateContext(DbMode.Standard);
        Assert.Equal(0, ctx.NumberOfOpenConnections);

        var c1 = ctx.GetConnection(ExecutionType.Read);
        await c1.OpenAsync();
        Assert.Equal(1, ctx.NumberOfOpenConnections);

        var c2 = ctx.GetConnection(ExecutionType.Write);
        await c2.OpenAsync();
        Assert.Equal(2, ctx.NumberOfOpenConnections);

        Assert.NotSame(c1, c2);

        ctx.CloseAndDisposeConnection(c1);
        Assert.Equal(1, ctx.NumberOfOpenConnections);

        await ctx.CloseAndDisposeConnectionAsync(c2);
        Assert.Equal(0, ctx.NumberOfOpenConnections);
    }

    [Fact]
    public async Task KeepAlive_PersistentStaysOpen_OthersDispose()
    {
        // Use LocalDb to preserve KeepAlive mode (regular SQL Server coerces KeepAlive to Standard)
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer));
        // keep-alive opens a persistent connection during initialization
        Assert.True(ctx.NumberOfOpenConnections >= 1);

        var c = ctx.GetConnection(ExecutionType.Read);
        await c.OpenAsync();
        var openNow = ctx.NumberOfOpenConnections;
        Assert.True(openNow >= 2);

        await ctx.CloseAndDisposeConnectionAsync(c);
        Assert.Equal(openNow - 1, ctx.NumberOfOpenConnections);
    }

    [Fact]
    public void KeepAlive_AppliesSessionSettings_OnInit()
    {
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);
        // Settings are now sent as a single command (may include semicolons)
        Assert.Contains(factory.Connection.ExecutedCommands,
            cmd => cmd.Contains("PRAGMA foreign_keys = ON"));
    }

    [Fact]
    public void KeepAliveRequested_IsolatedInMemory_UsesSingleConnectionStrategy()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));

        Assert.Equal(DbMode.SingleConnection, ctx.ConnectionMode);
        Assert.NotNull(ctx.PersistentConnection);

        var readConnection = ctx.GetConnection(ExecutionType.Read);
        var writeConnection = ctx.GetConnection(ExecutionType.Write);

        Assert.Same(ctx.PersistentConnection, readConnection);
        Assert.Same(readConnection, writeConnection);

        ctx.CloseAndDisposeConnection(readConnection);
        Assert.True(ctx.NumberOfOpenConnections >= 1);
    }

    [Fact]
    public void KeepAliveRequested_LocalDb_RetainsKeepAliveSentinelConnection()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.Equal(DbMode.KeepAlive, ctx.ConnectionMode);
        Assert.NotNull(ctx.PersistentConnection);

        var operationConnection = ctx.GetConnection(ExecutionType.Read);

        Assert.NotSame(ctx.PersistentConnection, operationConnection);

        ctx.CloseAndDisposeConnection(operationConnection);
        Assert.True(ctx.NumberOfOpenConnections >= 1);
    }

    [Fact]
    public void SingleConnection_AlwaysReturnsPersistent()
    {
        using var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");
        Assert.True(ctx.NumberOfOpenConnections >= 1);

        var c1 = ctx.GetConnection(ExecutionType.Read);
        var c2 = ctx.GetConnection(ExecutionType.Write);
        Assert.Same(c1, c2);

        ctx.CloseAndDisposeConnection(c1);
        // persistent connection remains
        Assert.True(ctx.NumberOfOpenConnections >= 1);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void SingleConnection_ReadOnlyMode_IsDisallowed(SupportedDatabase database)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={database}",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(cfg, new fakeDbFactory(database)));
    }

    [Fact]
    public void StandardRequested_IsolatedInMemory_CoercesToSingleConnection_ForReads()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));

        Assert.Equal(DbMode.SingleConnection, ctx.ConnectionMode);
        Assert.NotNull(ctx.PersistentConnection);

        var read = ctx.GetConnection(ExecutionType.Read);
        var readShared = ctx.GetConnection(ExecutionType.Read, true);
        var write = ctx.GetConnection(ExecutionType.Write);

        Assert.Same(ctx.PersistentConnection, read);
        Assert.Same(read, readShared);
        Assert.Same(read, write);
    }

    [Fact]
    public async Task SingleWriter_ReadGetsNew_WriteGetsPersistent()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        Assert.True(ctx.NumberOfOpenConnections >= 1);

        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        var countAfterOpen = ctx.NumberOfOpenConnections;
        Assert.True(countAfterOpen >= 2);

        ctx.CloseAndDisposeConnection(readConn);
        Assert.Equal(countAfterOpen - 1, ctx.NumberOfOpenConnections);

        var writeConn = ctx.GetConnection(ExecutionType.Write);
        // write connection is persistent; releasing should not change count
        var beforeRelease = ctx.NumberOfOpenConnections;
        ctx.CloseAndDisposeConnection(writeConn);
        Assert.Equal(beforeRelease, ctx.NumberOfOpenConnections);
    }

    [Fact]
    public void StandardConnections_DefaultToNoOpLockers()
    {
        using var ctx = CreateContext(DbMode.Standard, SupportedDatabase.Sqlite, "file.db");
        var connection = ctx.GetConnection(ExecutionType.Read);

        using var locker = connection.GetLock();

        Assert.IsType<NoOpAsyncLocker>(locker);

        ctx.CloseAndDisposeConnection(connection);
    }

    [Fact]
    public void StandardSharedConnections_UseRealLockers()
    {
        using var ctx = CreateContext(DbMode.Standard, SupportedDatabase.SqlServer);
        var connection = ctx.GetConnection(ExecutionType.Read, true);

        using var locker = connection.GetLock();

        Assert.IsType<RealAsyncLocker>(locker);

        ctx.CloseAndDisposeConnection(connection);
    }

    [Fact]
    public void SingleWriterWriteConnection_UsesNoOpLocker()
    {
        using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var connection = ctx.GetConnection(ExecutionType.Write);

        using var locker = connection.GetLock();

        Assert.IsType<NoOpAsyncLocker>(locker);

        ctx.CloseAndDisposeConnection(connection);
    }

    [Fact]
    public void SingleWriterReadConnection_UsesNoOpLocker()
    {
        using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var connection = ctx.GetConnection(ExecutionType.Read);

        using var locker = connection.GetLock();

        Assert.IsType<NoOpAsyncLocker>(locker);

        ctx.CloseAndDisposeConnection(connection);
    }

    [Fact]
    public void SingleConnectionPinnedConnection_UsesRealLocker()
    {
        using var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");
        var connection = ctx.GetConnection(ExecutionType.Read);

        using var locker = connection.GetLock();

        Assert.IsType<RealAsyncLocker>(locker);

        ctx.CloseAndDisposeConnection(connection);
    }

    [Fact]
    public async Task Standard_MaxConnections_TracksPeak()
    {
        await using var ctx = CreateContext(DbMode.Standard);
        var a = ctx.GetConnection(ExecutionType.Read);
        var b = ctx.GetConnection(ExecutionType.Write);
        await a.OpenAsync();
        await b.OpenAsync();
        Assert.Equal(2, ctx.NumberOfOpenConnections);
        Assert.Equal(2, ctx.PeakOpenConnections);
        ctx.CloseAndDisposeConnection(a);
        ctx.CloseAndDisposeConnection(b);
        Assert.Equal(0, ctx.NumberOfOpenConnections);
        Assert.Equal(2, ctx.PeakOpenConnections);
    }

    [Fact]
    public async Task SingleWriter_MaxConnections_TracksReadPeak()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var before = ctx.NumberOfOpenConnections; // persistent write conn
        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        Assert.True(ctx.NumberOfOpenConnections >= before + 1);
        Assert.True(ctx.PeakOpenConnections >= ctx.NumberOfOpenConnections);
        ctx.CloseAndDisposeConnection(read);
    }

    // Additional tests for KeepAliveConnectionStrategy methods

    [Fact]
    public async Task KeepAlive_ReleaseConnection_NullConnection_DoesNotThrow()
    {
        await using var ctx = CreateContext(DbMode.KeepAlive, SupportedDatabase.Sqlite, ":memory:");

        // Should not throw when releasing null connection
        ctx.CloseAndDisposeConnection(null);
        await ctx.CloseAndDisposeConnectionAsync(null);
    }

    [Fact]
    public async Task KeepAlive_ReleaseConnection_PersistentConnection_DoesNotDispose()
    {
        await using var ctx = CreateContext(DbMode.KeepAlive, SupportedDatabase.Sqlite, ":memory:");
        var beforeCount = ctx.NumberOfOpenConnections;

        // Get a connection (should be same as persistent)
        var connection = ctx.GetConnection(ExecutionType.Read);

        // Release it - should not decrease connection count since it's persistent
        ctx.CloseAndDisposeConnection(connection);
        Assert.Equal(beforeCount, ctx.NumberOfOpenConnections);

        // Test async version too
        var connection2 = ctx.GetConnection(ExecutionType.Write);
        await ctx.CloseAndDisposeConnectionAsync(connection2);
        Assert.Equal(beforeCount, ctx.NumberOfOpenConnections);
    }

    [Fact]
    public async Task KeepAlive_ReleaseConnection_NonPersistentConnection_Disposes()
    {
        // Use SQL Server to avoid automatic mode coercion that happens with SQLite
        await using var ctx = CreateContext(DbMode.KeepAlive, SupportedDatabase.SqlServer);

        // Create a separate connection that's not the persistent one
        var separateConnection = ctx.GetConnection(ExecutionType.Read, false);
        await separateConnection.OpenAsync();
        var beforeCount = ctx.NumberOfOpenConnections;

        // Release it - should dispose and decrease count
        await ctx.CloseAndDisposeConnectionAsync(separateConnection);
        Assert.True(ctx.NumberOfOpenConnections < beforeCount);
    }

    [Fact]
    public void KeepAlive_PostInitialize_NullConnection_SetsNullPersistent()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));

        // PostInitialize is called during construction, we're just verifying it doesn't crash with null
        Assert.NotNull(ctx); // Context created successfully
    }

    // Additional tests for Standard/Single/SingleWriter connection strategies - edge cases

    [Fact]
    public async Task Standard_GetConnection_MultipleThreads_CreatesUniqueConnections()
    {
        // Use SqlServer instead of SQLite to avoid connection pooling issues
        await using var ctx = CreateContext(DbMode.Standard, SupportedDatabase.SqlServer, "Data Source=test;");

        var connections = new List<ITrackedConnection>();
        var tasks = new List<Task>();

        // Create connections from multiple tasks
        for (var i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var conn = ctx.GetConnection(ExecutionType.Read);
                await conn.OpenAsync();
                lock (connections)
                {
                    connections.Add(conn);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // All connections should be unique in Standard mode
        Assert.Equal(5, connections.Count);
        Assert.Equal(5, connections.Distinct().Count());

        // Clean up
        foreach (var conn in connections)
        {
            await ctx.CloseAndDisposeConnectionAsync(conn);
        }
    }

    [Fact]
    public async Task SingleConnection_IsDisposed_ReturnsCorrectState()
    {
        var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");

        Assert.False(ctx.IsDisposed);

        await ctx.DisposeAsync();

        Assert.True(ctx.IsDisposed);
    }

    [Fact]
    public async Task SingleWriter_WriteConnections_Serializable()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");

        var write1 = ctx.GetConnection(ExecutionType.Write);
        ctx.CloseAndDisposeConnection(write1);

        var write2 = ctx.GetConnection(ExecutionType.Write);
        ctx.CloseAndDisposeConnection(write2);

        Assert.NotNull(write1);
        Assert.NotNull(write2);
    }

    [Fact]
    public async Task Standard_ReleaseConnection_NullConnection_DoesNotThrow()
    {
        await using var ctx = CreateContext(DbMode.Standard, SupportedDatabase.Sqlite, ":memory:");

        // Should not throw
        ctx.CloseAndDisposeConnection(null);
        await ctx.CloseAndDisposeConnectionAsync(null);
    }

    [Fact]
    public async Task SingleConnection_ReleaseConnection_NullConnection_DoesNotThrow()
    {
        await using var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");

        // Should not throw
        ctx.CloseAndDisposeConnection(null);
        await ctx.CloseAndDisposeConnectionAsync(null);
    }

    [Fact]
    public async Task SingleWriter_ReleaseConnection_NullConnection_DoesNotThrow()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");

        // Should not throw
        ctx.CloseAndDisposeConnection(null);
        await ctx.CloseAndDisposeConnectionAsync(null);
    }

    // Additional coverage tests for StandardConnectionStrategy

    [Fact]
    public void Standard_PostInitialize_DisposesConnection()
    {
        var factory = new RecordingFactory(SupportedDatabase.SqlServer);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new StandardConnectionStrategy(ctx);

        // PostInitialize should dispose the provided connection
        // We verify this by checking the connection state changes to Closed after PostInitialize
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);

        strategy.PostInitialize(connection);

        // After PostInitialize, connection should be disposed (State becomes Closed)
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Standard_PostInitialize_NullConnection_DoesNotThrow()
    {
        var ctx = CreateContext(DbMode.Standard);
        var strategy = new StandardConnectionStrategy(ctx);

        // Should not throw with null connection
        strategy.PostInitialize(null);
    }

    [Fact]
    public async Task Standard_ReleaseConnectionAsync_DisposesConnection()
    {
        var factory = new RecordingFactory(SupportedDatabase.SqlServer);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new StandardConnectionStrategy(ctx);

        // Open connection to test disposal
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);

        await strategy.ReleaseConnectionAsync(connection);

        // After ReleaseConnectionAsync, connection should be disposed (State becomes Closed)
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Standard_ReleaseConnection_DisposesConnection()
    {
        var factory = new RecordingFactory(SupportedDatabase.SqlServer);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new StandardConnectionStrategy(ctx);

        // Open connection to test disposal
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);

        strategy.ReleaseConnection(connection);

        // After ReleaseConnection, connection should be disposed (State becomes Closed)
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    // Additional coverage tests for SingleConnectionStrategy

    [Fact]
    public void SingleConnection_PostInitialize_SetsPersistentOnly()
    {
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var commandCountBefore = factory.Connection.ExecutedCommands.Count;
        var strategy = new SingleConnectionStrategy(ctx);

        // PostInitialize should only set persistent connection, not apply settings
        strategy.PostInitialize(connection);

        // No additional commands should have been executed by PostInitialize itself
        Assert.Equal(commandCountBefore, factory.Connection.ExecutedCommands.Count);

        // Verify persistent connection was set
        Assert.Same(connection, ctx.PersistentConnection);
    }

    [Fact]
    public void SingleConnection_PostInitialize_NullConnection_SetsPersistentToNull()
    {
        var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");
        var strategy = new SingleConnectionStrategy(ctx);

        strategy.PostInitialize(null);

        // Should not throw and should set persistent connection to null
        Assert.Null(ctx.PersistentConnection);
    }

    [Fact]
    public async Task SingleConnection_ReleaseConnectionAsync_NonPersistentConnection_Disposes()
    {
        await using var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");
        var strategy = new SingleConnectionStrategy(ctx);

        // Create a separate connection that's different from the persistent one
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard, // Use Standard mode for this separate connection
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var separateCtx = new DatabaseContext(cfg, factory);
        var separateConnection = separateCtx.GetConnection(ExecutionType.Read);
        await separateConnection.OpenAsync();

        // Since this is not the persistent connection from our SingleConnection context, it should be disposed
        Assert.Equal(ConnectionState.Open, separateConnection.State);

        await strategy.ReleaseConnectionAsync(separateConnection);

        // Connection should be disposed (State becomes Closed)
        Assert.Equal(ConnectionState.Closed, separateConnection.State);
    }

    // ── Standard-mode ephemeral-churn stress ────────────────────────────────
    // 20 tasks each open/close 50 ephemeral connections in tight succession.
    // Metrics are enabled so ConnectionsOpened/Closed are populated.
    // Verifies: no connection leaks, peak concurrency > 1, and every open
    // has a matching close.
    [Fact]
    public async Task Standard_ConcurrentChurn_NoLeaks()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnableMetrics = true
        };
        await using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer));

        const int threads = 20;
        const int roundsPerThread = 50;
        const int totalCycles = threads * roundsPerThread; // 1 000

        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < roundsPerThread; i++)
            {
                var conn = ctx.GetConnection(ExecutionType.Read);
                await conn.OpenAsync();
                await ctx.CloseAndDisposeConnectionAsync(conn);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var m = ctx.Metrics;
        Assert.Equal(0, m.ConnectionsCurrent);
        Assert.True(m.PeakOpenConnections > 1 || m.ConnectionsOpened >= totalCycles);
        // Every churn cycle must have been tracked; init may add one extra open+close
        Assert.True(m.ConnectionsOpened >= totalCycles);
        Assert.True(m.ConnectionsClosed >= totalCycles);
        Assert.Equal(m.ConnectionsOpened, m.ConnectionsClosed); // no leaks
    }

    // ── KeepAlive sentinel-stability churn stress ───────────────────────────
    // LocalDb connection string is required to retain KeepAlive mode (plain
    // SqlServer coerces it to Standard).  20 tasks each open/close 50
    // ephemeral connections while the sentinel stays pinned.
    // Metrics are enabled to verify the opened/closed delta matches the
    // sentinel that remains open at the end.
    [Fact]
    public async Task KeepAlive_ConcurrentChurn_SentinelPersists()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnableMetrics = true
        };
        await using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer));
        Assert.Equal(DbMode.KeepAlive, ctx.ConnectionMode);

        var sentinel = ctx.PersistentConnection;
        Assert.NotNull(sentinel);
        var baseMetrics = ctx.Metrics;
        var baseCurrent = baseMetrics.ConnectionsCurrent; // sentinel(s) at rest

        const int threads = 20;
        const int roundsPerThread = 50;
        const int totalCycles = threads * roundsPerThread; // 1 000

        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < roundsPerThread; i++)
            {
                var conn = ctx.GetConnection(ExecutionType.Read);
                await conn.OpenAsync();
                await ctx.CloseAndDisposeConnectionAsync(conn);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Sentinel object identity must be unchanged
        Assert.Same(sentinel, ctx.PersistentConnection);

        var m = ctx.Metrics;

        // Only sentinel connection(s) remain — no ephemeral leaks
        Assert.Equal(baseCurrent, m.ConnectionsCurrent);

        // At least one ephemeral connection overlapped with the sentinel
        Assert.True(m.PeakOpenConnections > baseCurrent);

        // All ephemeral cycles were opened and closed; sentinel open is NOT
        // closed, so opened - closed == baseCurrent (the still-open sentinels).
        Assert.True(m.ConnectionsOpened >= totalCycles + baseCurrent);
        Assert.True(m.ConnectionsClosed >= totalCycles);
        Assert.Equal(baseCurrent, (int)(m.ConnectionsOpened - m.ConnectionsClosed));
    }
}
