#region

using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
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
}
