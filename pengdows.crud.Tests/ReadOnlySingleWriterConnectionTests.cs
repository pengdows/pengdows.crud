using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class ReadOnlySingleWriterConnectionTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> Commands { get; } = new();
        public List<string> ConnectionStrings { get; } = new();
        
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this, Commands);

        [AllowNull]
        public override string ConnectionString
        {
            get => base.ConnectionString;
            set
            {
                var normalized = value ?? string.Empty;
                ConnectionStrings.Add(normalized);
                base.ConnectionString = normalized;
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

    private static DatabaseContext CreateReadOnlySingleWriterContext(RecordingFactory factory)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        return new DatabaseContext(config, factory);
    }

    private static DatabaseContext CreateReadOnlySingleConnectionContext(RecordingFactory factory)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task ReadOnlySingleWriter_PersistentConnection_ShouldHaveReadOnlySettings()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateReadOnlySingleWriterContext(factory);

        // Get any connection to trigger persistent connection creation
        var conn = ctx.GetConnection(ExecutionType.Write);
        await conn.OpenAsync();
        
        Assert.Single(factory.Connections);
        var persistentConnection = factory.Connections[0];
        
        // The persistent connection MUST have read-only settings applied even for ExecutionType.Write
        // because the context itself is ReadOnly
        Assert.Contains(persistentConnection.Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task ReadOnlySingleConnection_PersistentConnection_ShouldHaveReadOnlySettings()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateReadOnlySingleConnectionContext(factory);

        // Get any connection to trigger persistent connection creation
        var conn = ctx.GetConnection(ExecutionType.Write);
        await conn.OpenAsync();
        
        Assert.Single(factory.Connections);
        var persistentConnection = factory.Connections[0];
        
        // The persistent connection MUST have read-only settings applied even for ExecutionType.Write
        // because the context itself is ReadOnly
        Assert.Contains(persistentConnection.Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task ReadOnlySingleWriter_AllConnections_ShouldBeReadOnly()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateReadOnlySingleWriterContext(factory);

        // Get write connection (should be the persistent connection)
        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        
        // Get read connection (should create a new read-only connection)
        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        
        Assert.Equal(2, factory.Connections.Count);
        
        // BOTH connections should have read-only settings because the context is ReadOnly
        var persistentConnection = factory.Connections[0];
        var readOnlyConnection = factory.Connections[1];
        
        Assert.Contains(persistentConnection.Commands, c => c.Contains("query_only"));
        Assert.Contains(readOnlyConnection.Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task ReadOnlySingleWriter_WriteTransaction_ShouldThrow()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateReadOnlySingleWriterContext(factory);

        // Attempting to create a write transaction on a read-only context should throw
        await Assert.ThrowsAsync<NotSupportedException>(() => 
        {
            var tx = ctx.BeginTransaction(readOnly: false);
            return tx.DisposeAsync().AsTask();
        });
    }

    [Fact]
    public async Task ReadOnlySingleWriter_WriteOperations_ShouldFail()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateReadOnlySingleWriterContext(factory);

        // Get connection and try to perform write operation
        var conn = ctx.GetConnection(ExecutionType.Write);
        await conn.OpenAsync();
        
        await using var container = ctx.CreateSqlContainer("INSERT INTO t VALUES (1)");
        
        // Should fail because context is read-only
        await Assert.ThrowsAsync<NotSupportedException>(() => container.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ReadOnlySingleConnection_WriteOperations_ShouldFail()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateReadOnlySingleConnectionContext(factory);

        // Get connection and try to perform write operation
        var conn = ctx.GetConnection(ExecutionType.Write);
        await conn.OpenAsync();
        
        await using var container = ctx.CreateSqlContainer("INSERT INTO t VALUES (1)");
        
        // Should fail because context is read-only
        await Assert.ThrowsAsync<NotSupportedException>(() => container.ExecuteNonQueryAsync());
    }

    [Theory]
    [InlineData(DbMode.SingleWriter)]
    [InlineData(DbMode.SingleConnection)]
    public async Task ReadOnlySingleMode_AssertIsWriteConnection_ShouldFail(DbMode mode)
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = mode == DbMode.SingleWriter ? "Data Source=file.db;EmulatedProduct=Sqlite" : "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = mode,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        
        await using var ctx = new DatabaseContext(config, factory);

        // Should fail because the context is read-only
        Assert.Throws<InvalidOperationException>(() => ctx.AssertIsWriteConnection());
    }
}
