using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DuckDbReadIntentConnectionTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> ConnectionStrings { get; } = new();
        public List<string> Commands { get; } = new();

        [AllowNull]
        public override string ConnectionString
        {
            get => base.ConnectionString;
            set
            {
                base.ConnectionString = value;
                ConnectionStrings.Add(value ?? string.Empty);
            }
        }

        protected override DbCommand CreateDbCommand()
        {
            var cmd = new RecordingCommand();
            cmd.Connection = this;
            return cmd;
        }

        private class RecordingCommand : fakeDbCommand
        {
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                var conn = (RecordingConnection)Connection!;
                conn.Commands.Add(CommandText);
                return base.ExecuteDbDataReader(behavior);
            }

            public override Task<int> ExecuteNonQueryAsync(System.Threading.CancellationToken cancellationToken)
            {
                var conn = (RecordingConnection)Connection!;
                conn.Commands.Add(CommandText);
                return base.ExecuteNonQueryAsync(cancellationToken);
            }
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
    }

    [Fact]
    public async Task ReadIntent_DuckDb_ReadOnly_UsesConnectionStringParam()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        // Use correct constructor: (IDatabaseContextConfiguration, DbProviderFactory, ILoggerFactory)
        await using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Standard READ operation
        var read = ctx.GetConnection(ExecutionType.Read);
        await read.OpenAsync();
        ctx.CloseAndDisposeConnection(read);

        var allConnectionStrings = factory.Connections.SelectMany(c => c.ConnectionStrings);
        var allCommands = factory.Connections.SelectMany(c => c.Commands);

        // Verify it IS in the connection string
        Assert.Contains(allConnectionStrings,
            cs => cs.Contains("access_mode=READ_ONLY", StringComparison.OrdinalIgnoreCase));
        
        // Verify it is NOT sent as a SQL command (optimized)
        Assert.DoesNotContain(allCommands,
            cmd => cmd.Contains("access_mode", StringComparison.OrdinalIgnoreCase));
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

        // Use correct constructor: (IDatabaseContextConfiguration, DbProviderFactory, ILoggerFactory)
        await using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Explicitly request WRITE to ensure READ ONLY settings are NOT applied
        var write = ctx.GetConnection(ExecutionType.Write);
        await write.OpenAsync();
        ctx.CloseAndDisposeConnection(write);

        var allConnectionStrings = factory.Connections.SelectMany(c => c.ConnectionStrings);
        var allCommands = factory.Connections.SelectMany(c => c.Commands);

        Assert.DoesNotContain(allConnectionStrings,
            cs => cs.Contains("access_mode=READ_ONLY", StringComparison.OrdinalIgnoreCase));
        
        // Final session settings are empty for DuckDB even on write
        Assert.DoesNotContain(allCommands,
            cmd => cmd.Contains("access_mode", StringComparison.OrdinalIgnoreCase));
    }
}
