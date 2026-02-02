#region

using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SingleWriterReadOnlyConnectionTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> Commands { get; } = new();

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, Commands);
        }
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly List<string> _commands;

        public RecordingCommand(fakeDbConnection connection, List<string> commands) : base(connection)
        {
            _commands = commands;
        }

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

        public override DbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new fakeDbParameter();
        }
    }

    private static DatabaseContext CreateContext(RecordingFactory factory)
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
    public async Task ReadConnection_AppliesReadOnlyPreamble()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateContext(factory);

        // SingleWriter now uses per-operation connections (not persistent)
        // First connection is for dialect detection during init (disposed)
        // Read connection is the next one
        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        ctx.CloseAndDisposeConnection(read); // Must dispose to release permit

        // Find the read connection (last one with query_only)
        var readConn = factory.Connections.Find(c => c.Commands.Exists(cmd => cmd.Contains("query_only")));
        Assert.NotNull(readConn);
    }

    [Fact]
    public async Task WriteConnection_DoesNotApplyReadOnlyPreamble()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateContext(factory);

        // Get and release write connection
        var write = ctx.GetConnection(ExecutionType.Write);
        await write.OpenAsync();
        Assert.DoesNotContain(factory.Connections.Last().Commands, c => c.Contains("query_only"));
        ctx.CloseAndDisposeConnection(write); // Must dispose to release permit + turnstile

        // Now get read connection
        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        ctx.CloseAndDisposeConnection(read);

        // Find a connection with query_only (should be the read connection)
        var readConn = factory.Connections.Find(c => c.Commands.Exists(cmd => cmd.Contains("query_only")));
        Assert.NotNull(readConn);
    }
}
