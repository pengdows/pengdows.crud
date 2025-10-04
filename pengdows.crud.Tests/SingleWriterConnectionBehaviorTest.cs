using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SingleWriterConnectionBehaviorTest
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> Commands { get; } = new();
        public List<string> ConnectionStrings { get; } = new();
        
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this, Commands);

        public override string ConnectionString 
        { 
            get => base.ConnectionString;
            set 
            { 
                ConnectionStrings.Add(value);
                base.ConnectionString = value; 
            }
        }
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly List<string> _commands;
        public RecordingCommand(fakeDbConnection connection, List<string> commands) : base(connection) => _commands = commands;
        public override int ExecuteNonQuery()
        {
            _commands.Add(CommandText);
            return base.ExecuteNonQuery();
        }
    }

    private sealed class RecordingFactory : DbProviderFactory
    {
        public List<RecordingConnection> Connections { get; } = new();
        public override DbConnection CreateConnection()
        {
            var conn = new RecordingConnection();
            Connections.Add(conn);
            return conn;
        }

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }

    private static DatabaseContext CreateSingleWriterContext(RecordingFactory factory)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task SingleWriter_AllNewConnections_ShouldBeReadOnly()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateSingleWriterContext(factory);

        // Get a write connection first - this should create the persistent connection
        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        
        Assert.Single(factory.Connections);
        var persistentConnection = factory.Connections[0];
        
        // The persistent connection should NOT have read-only settings
        Assert.DoesNotContain(persistentConnection.Commands, c => c.Contains("query_only"));
        
        // Now get a read connection - this should create a NEW read-only connection
        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        
        Assert.Equal(2, factory.Connections.Count);
        var readOnlyConnection = factory.Connections[1];
        
        // The new read connection MUST have read-only settings applied
        Assert.Contains(readOnlyConnection.Commands, c => c.Contains("query_only"));
        
        // Get another read connection - should create another read-only connection
        var readConn2 = ctx.GetConnection(ExecutionType.Read);
        await readConn2.OpenAsync();
        
        Assert.Equal(3, factory.Connections.Count);
        var readOnlyConnection2 = factory.Connections[2];
        
        // This new read connection MUST also have read-only settings applied
        Assert.Contains(readOnlyConnection2.Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task SingleWriter_WriteTransaction_UsesSharedConnection()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateSingleWriterContext(factory);

        // Create a write transaction - should use the shared persistent connection
        await using var tx = ctx.BeginTransaction(readOnly: false);
        
        Assert.Single(factory.Connections);
        var persistentConnection = factory.Connections[0];
        
        // Should NOT have read-only settings
        Assert.DoesNotContain(persistentConnection.Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task SingleWriter_ReadOnlyTransaction_CreatesReadOnlyConnection()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateSingleWriterContext(factory);

        // Create the persistent connection first
        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        
        Assert.Single(factory.Connections);
        
        // Create a read-only transaction - should create a separate read-only connection
        await using var tx = ctx.BeginTransaction(readOnly: true);
        
        Assert.Equal(2, factory.Connections.Count);
        var readOnlyConnection = factory.Connections[1];
        
        // MUST have read-only settings applied
        Assert.Contains(readOnlyConnection.Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task SingleWriter_OnlyPersistentConnection_CanWrite()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateSingleWriterContext(factory);

        // Get multiple connections
        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        
        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        
        Assert.Equal(2, factory.Connections.Count);
        var persistentConnection = factory.Connections[0];
        var readOnlyConnection = factory.Connections[1];
        
        // Only the persistent connection should be writable (no query_only pragma)
        Assert.DoesNotContain(persistentConnection.Commands, c => c.Contains("query_only"));
        
        // All other connections MUST be read-only
        Assert.Contains(readOnlyConnection.Commands, c => c.Contains("query_only"));
    }
}