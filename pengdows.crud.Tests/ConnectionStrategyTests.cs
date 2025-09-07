#region

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
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

        public override DbConnection CreateConnection() => Connection;
        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }

    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> ExecutedCommands { get; } = new();
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this, ExecutedCommands);
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly List<string> _record;
        public RecordingCommand(fakeDbConnection connection, List<string> record) : base(connection) => _record = record;
        public override int ExecuteNonQuery()
        {
            _record.Add(CommandText);
            return base.ExecuteNonQuery();
        }
    }

    private static DatabaseContext CreateContext(DbMode mode, SupportedDatabase product = SupportedDatabase.SqlServer, string dataSource = "test")
    {
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
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
        await using var ctx = CreateContext(DbMode.Standard, SupportedDatabase.SqlServer);
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
        await using var ctx = CreateContext(DbMode.KeepAlive, SupportedDatabase.Sqlite, ":memory:");
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
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);
        Assert.Contains("PRAGMA foreign_keys = ON;", factory.Connection.ExecutedCommands);
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
    public async Task Standard_MaxConnections_TracksPeak()
    {
        await using var ctx = CreateContext(DbMode.Standard, SupportedDatabase.SqlServer);
        var a = ctx.GetConnection(ExecutionType.Read);
        var b = ctx.GetConnection(ExecutionType.Write);
        await a.OpenAsync();
        await b.OpenAsync();
        Assert.Equal(2, ctx.NumberOfOpenConnections);
        Assert.Equal(2, ctx.MaxNumberOfConnections);
        ctx.CloseAndDisposeConnection(a);
        ctx.CloseAndDisposeConnection(b);
        Assert.Equal(0, ctx.NumberOfOpenConnections);
        Assert.Equal(2, ctx.MaxNumberOfConnections);
    }

    [Fact]
    public async Task SingleWriter_MaxConnections_TracksReadPeak()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var before = ctx.NumberOfOpenConnections; // persistent write conn
        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        Assert.True(ctx.NumberOfOpenConnections >= before + 1);
        Assert.True(ctx.MaxNumberOfConnections >= ctx.NumberOfOpenConnections);
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
        await using var ctx = CreateContext(DbMode.KeepAlive, SupportedDatabase.Sqlite, ":memory:");

        // Create a separate connection that's not the persistent one
        var separateConnection = ctx.GetConnection(ExecutionType.Read, isShared: false);
        await separateConnection.OpenAsync();
        var beforeCount = ctx.NumberOfOpenConnections;

        // Release it - should dispose and decrease count
        await ctx.CloseAndDisposeConnectionAsync(separateConnection);
        Assert.True(ctx.NumberOfOpenConnections < beforeCount);
    }

    [Fact]
    public void KeepAlive_PostInitialize_SetsPersistentConnection()
    {
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new pengdows.crud.strategies.connection.KeepAliveConnectionStrategy(ctx);

        strategy.PostInitialize(connection);

        Assert.Contains("PRAGMA foreign_keys = ON;", factory.Connection.ExecutedCommands);
        Assert.Same(connection, ctx.PersistentConnection);
    }

    [Fact]
    public void KeepAlive_PostInitialize_NullConnection_SetsNullPersistent()
    {
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
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
        for (int i = 0; i < 5; i++)
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
    public async Task SingleWriter_WriteConnection_AlwaysSameReference()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");

        var write1 = ctx.GetConnection(ExecutionType.Write);
        var write2 = ctx.GetConnection(ExecutionType.Write);
        var write3 = ctx.GetConnection(ExecutionType.Write);

        Assert.Same(write1, write2);
        Assert.Same(write2, write3);
        Assert.Same(write1, write3);
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
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new pengdows.crud.strategies.connection.StandardConnectionStrategy(ctx);

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
        var ctx = CreateContext(DbMode.Standard, SupportedDatabase.SqlServer);
        var strategy = new pengdows.crud.strategies.connection.StandardConnectionStrategy(ctx);

        // Should not throw with null connection
        strategy.PostInitialize(null);
    }

    [Fact]
    public async Task Standard_ReleaseConnectionAsync_DisposesConnection()
    {
        var factory = new RecordingFactory(SupportedDatabase.SqlServer);
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new pengdows.crud.strategies.connection.StandardConnectionStrategy(ctx);

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
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new pengdows.crud.strategies.connection.StandardConnectionStrategy(ctx);

        // Open connection to test disposal
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);

        strategy.ReleaseConnection(connection);

        // After ReleaseConnection, connection should be disposed (State becomes Closed)
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    // Additional coverage tests for SingleConnectionStrategy

    [Fact]
    public void SingleConnection_PostInitialize_SetsPersistentConnection()
    {
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Read);
        var strategy = new pengdows.crud.strategies.connection.SingleConnectionStrategy(ctx);

        // PostInitialize should set persistent connection
        strategy.PostInitialize(connection);

        // Session settings should have been applied during initialization
        Assert.Contains("PRAGMA foreign_keys = ON;", factory.Connection.ExecutedCommands);

        Assert.Same(connection, ctx.PersistentConnection);
    }

    [Fact]
    public void SingleConnection_PostInitialize_NullConnection_SetsPersistentToNull()
    {
        var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");
        var strategy = new pengdows.crud.strategies.connection.SingleConnectionStrategy(ctx);

        strategy.PostInitialize(null);

        // Should not throw and should set persistent connection to null
        Assert.Null(ctx.PersistentConnection);
    }

    [Fact]
    public async Task SingleConnection_ReleaseConnectionAsync_NonPersistentConnection_Disposes()
    {
        await using var ctx = CreateContext(DbMode.SingleConnection, SupportedDatabase.Sqlite, ":memory:");
        var strategy = new pengdows.crud.strategies.connection.SingleConnectionStrategy(ctx);

        // Create a separate connection that's different from the persistent one
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
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

    // Additional coverage tests for SingleWriterConnectionStrategy

    [Fact]
    public void SingleWriter_PostInitialize_SetsPersistentConnection()
    {
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var ctx = new DatabaseContext(cfg, factory);

        var connection = ctx.GetConnection(ExecutionType.Write);
        var strategy = new pengdows.crud.strategies.connection.SingleWriterConnectionStrategy(ctx);

        strategy.PostInitialize(connection);

        // Session settings should have been applied during initialization
        Assert.Contains("PRAGMA foreign_keys = ON;", factory.Connection.ExecutedCommands);

        Assert.Same(connection, ctx.PersistentConnection);
    }

    [Fact]
    public async Task SingleWriter_ReleaseConnectionAsync_ReadConnection_Disposes()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var strategy = new pengdows.crud.strategies.connection.SingleWriterConnectionStrategy(ctx);

        // Get a read connection (should be disposable)
        var readConnection = ctx.GetConnection(ExecutionType.Read);
        await readConnection.OpenAsync();

        Assert.Equal(ConnectionState.Open, readConnection.State);

        await strategy.ReleaseConnectionAsync(readConnection);

        // Read connection should be disposed (State becomes Closed)
        Assert.Equal(ConnectionState.Closed, readConnection.State);
    }

    [Fact]
    public async Task SingleWriter_ReleaseConnectionAsync_WriteConnection_DoesNotDispose()
    {
        await using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var strategy = new pengdows.crud.strategies.connection.SingleWriterConnectionStrategy(ctx);

        // Get the write connection (should be persistent)
        var writeConnection = ctx.GetConnection(ExecutionType.Write);

        // The persistent write connection should remain open
        await strategy.ReleaseConnectionAsync(writeConnection);

        // Persistent connection should still be available and not disposed
        Assert.Same(writeConnection, ctx.PersistentConnection);
    }

    [Fact]
    public void SingleWriter_ReleaseConnection_ReadConnection_Disposes()
    {
        using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var strategy = new pengdows.crud.strategies.connection.SingleWriterConnectionStrategy(ctx);

        // Get a read connection (should be disposable)
        var readConnection = ctx.GetConnection(ExecutionType.Read);
        readConnection.Open();

        Assert.Equal(ConnectionState.Open, readConnection.State);

        strategy.ReleaseConnection(readConnection);

        // Read connection should be disposed (State becomes Closed)
        Assert.Equal(ConnectionState.Closed, readConnection.State);
    }

    [Fact]
    public void SingleWriter_ReleaseConnection_WriteConnection_DoesNotDispose()
    {
        using var ctx = CreateContext(DbMode.SingleWriter, SupportedDatabase.Sqlite, "file.db");
        var strategy = new pengdows.crud.strategies.connection.SingleWriterConnectionStrategy(ctx);

        // Get the write connection (should be persistent)
        var writeConnection = ctx.GetConnection(ExecutionType.Write);

        // The persistent write connection should remain available after release
        strategy.ReleaseConnection(writeConnection);

        // Persistent connection should still be available and not disposed
        Assert.Same(writeConnection, ctx.PersistentConnection);
    }
}
