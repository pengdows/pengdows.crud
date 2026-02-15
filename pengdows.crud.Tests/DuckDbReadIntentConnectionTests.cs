using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DuckDbReadIntentConnectionTests
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
                ConnectionStrings.Add(value ?? string.Empty);
                base.ConnectionString = value;
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

    [Fact]
    public async Task ReadIntent_DuckDb_ReadWrite_DoesNotUseReadOnlySettings()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(config, factory);

        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        ctx.CloseAndDisposeConnection(read);

        var allConnectionStrings = factory.Connections.SelectMany(c => c.ConnectionStrings);
        var allCommands = factory.Connections.SelectMany(c => c.Commands);

        Assert.DoesNotContain(allConnectionStrings,
            cs => cs.Contains("access_mode=READ_ONLY", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(allCommands,
            cmd => cmd.Contains("access_mode", StringComparison.OrdinalIgnoreCase));
    }
}
