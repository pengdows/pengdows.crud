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

public class DuckDbReadOnlyTransactionResetTests
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

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }

    private sealed class FailingResetConnection : fakeDbConnection
    {
        public List<string> Commands { get; } = new();

        protected override DbCommand CreateDbCommand()
        {
            return new FailingResetCommand(this, Commands);
        }
    }

    private sealed class FailingResetCommand : fakeDbCommand
    {
        private readonly List<string> _commands;

        public FailingResetCommand(fakeDbConnection connection, List<string> commands) : base(connection)
        {
            _commands = commands;
        }

        public override int ExecuteNonQuery()
        {
            if (!string.IsNullOrWhiteSpace(CommandText))
            {
                _commands.Add(CommandText);
                if (CommandText.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                    CommandText.Contains("read_write", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("reset failed");
                }
            }

            return base.ExecuteNonQuery();
        }
    }

    private sealed class FailingResetFactory : DbProviderFactory
    {
        public List<FailingResetConnection> Connections { get; } = new();

        public override DbConnection CreateConnection()
        {
            var conn = new FailingResetConnection();
            Connections.Add(conn);
            return conn;
        }

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }

    [Fact]
    public async Task DuckDb_ReadOnlyTransaction_Commit_ResetsAccessMode()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Commit();

        var allCommands = new List<string>();
        foreach (var conn in factory.Connections)
        {
            allCommands.AddRange(conn.Commands);
        }

        Assert.Contains(allCommands,
            c => c.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                 c.Contains("read_only", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(allCommands,
            c => c.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                 c.Contains("read_write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuckDb_ReadOnlyTransaction_Rollback_ResetsAccessMode()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Rollback();

        var allCommands = new List<string>();
        foreach (var conn in factory.Connections)
        {
            allCommands.AddRange(conn.Commands);
        }

        Assert.Contains(allCommands,
            c => c.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                 c.Contains("read_write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuckDb_ReadOnlyTransaction_ResetFailure_DoesNotPreventCleanup()
    {
        var factory = new FailingResetFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);

        var exception = Record.Exception(() => tx.Commit());

        Assert.Null(exception);

        FailingResetConnection? transactionConnection = null;
        foreach (var conn in factory.Connections)
        {
            foreach (var command in conn.Commands)
            {
                if (command.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                    command.Contains("read_only", StringComparison.OrdinalIgnoreCase))
                {
                    transactionConnection = conn;
                    break;
                }
            }

            if (transactionConnection != null)
            {
                break;
            }
        }

        Assert.NotNull(transactionConnection);
        Assert.True(transactionConnection!.DisposeCount > 0,
            "Connection should be disposed even when access-mode reset fails.");
    }

    [Fact]
    public async Task Sqlite_ReadOnlyTransaction_NoAccessModeReset()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Commit();

        var allCommands = new List<string>();
        foreach (var conn in factory.Connections)
        {
            allCommands.AddRange(conn.Commands);
        }

        Assert.DoesNotContain(allCommands,
            c => c.Contains("access_mode", StringComparison.OrdinalIgnoreCase));
    }
}
