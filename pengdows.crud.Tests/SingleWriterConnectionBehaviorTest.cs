using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, Commands);
        }

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

        var initialCount = factory.Connections.Count;

        // Acquire a write connection and assert it remains writable
        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        ctx.CloseAndDisposeConnection(writeConn);

        Assert.True(factory.Connections.Count > initialCount);
        var writeConnection = factory.Connections.Last();
        Assert.DoesNotContain(writeConnection.Commands, c => c.Contains("query_only"));

        // Acquire two read connections, each of which should be read-only
        for (var i = 0; i < 2; i++)
        {
            var readConn = ctx.GetConnection(ExecutionType.Read);
            await readConn.OpenAsync();
            ctx.CloseAndDisposeConnection(readConn);

            var readOnlyConnection = factory.Connections.Last();
            Assert.Contains(readOnlyConnection.Commands, c => c.Contains("query_only"));
        }
    }

    [Fact]
    public async Task SingleWriter_WriteTransaction_UsesWritableConnection()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateSingleWriterContext(factory);

        await using var tx = ctx.BeginTransaction(readOnly: false);

        Assert.True(factory.Connections.Count >= 1);
        var writeConnection = factory.Connections.Last();
        Assert.DoesNotContain(writeConnection.Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task SingleWriter_ReadConnectionsStayReadOnly()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateSingleWriterContext(factory);

        var writeConn = ctx.GetConnection(ExecutionType.Write);
        await writeConn.OpenAsync();
        ctx.CloseAndDisposeConnection(writeConn);
        var writerConnection = factory.Connections.Last();

        var readConn = ctx.GetConnection(ExecutionType.Read);
        await readConn.OpenAsync();
        ctx.CloseAndDisposeConnection(readConn);
        var readOnlyConnection = factory.Connections.Last();

        Assert.DoesNotContain(writerConnection.Commands, c => c.Contains("query_only"));
        Assert.Contains(readOnlyConnection.Commands, c => c.Contains("query_only"));
    }
}
