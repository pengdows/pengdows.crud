#region

using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SingleWriterReadOnlyTransactionTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> Commands { get; } = new();
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this, Commands);
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
    public async Task ReadOnlyTransaction_AppliesReadOnlyPreamble()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateContext(factory);

        await using (ctx.BeginTransaction(readOnly: true))
        {
        }

        Assert.Equal(2, factory.Connections.Count);
        Assert.Contains(factory.Connections[1].Commands, c => c.Contains("query_only"));
    }

    [Fact]
    public async Task ReadWriteTransaction_UsesWriterConnection()
    {
        var factory = new RecordingFactory();
        await using var ctx = CreateContext(factory);

        await using (ctx.BeginTransaction(readOnly: false))
        {
        }

        Assert.Single(factory.Connections);
        Assert.DoesNotContain(factory.Connections[0].Commands, c => c.Contains("query_only"));
    }
}
