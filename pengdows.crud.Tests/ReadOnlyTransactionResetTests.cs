using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that Oracle, MySQL, and MariaDB dialects reset read-only session state
/// on transaction commit/rollback so pooled connections are not left in read-only mode.
/// </summary>
public class ReadOnlyTransactionResetTests
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

    private static List<string> CollectCommands(RecordingFactory factory)
    {
        var all = new List<string>();
        foreach (var conn in factory.Connections)
        {
            all.AddRange(conn.Commands);
        }

        return all;
    }

    // ── Oracle ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Oracle_ReadOnlyTransaction_Commit_ResetsSession()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Oracle",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Commit();

        var commands = CollectCommands(factory);

        Assert.Contains(commands,
            c => c.Contains("READ ONLY", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(commands,
            c => c.Contains("READ WRITE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Oracle_ReadOnlyTransaction_Rollback_ResetsSession()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Oracle",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Rollback();

        var commands = CollectCommands(factory);

        Assert.Contains(commands,
            c => c.Contains("READ WRITE", StringComparison.OrdinalIgnoreCase));
    }

    // ── MySQL ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task MySql_ReadOnlyTransaction_Commit_ResetsSession()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=MySql",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Commit();

        var commands = CollectCommands(factory);

        Assert.Contains(commands,
            c => c.Contains("TRANSACTION READ ONLY", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(commands,
            c => c.Contains("TRANSACTION READ WRITE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MySql_ReadOnlyTransaction_Rollback_ResetsSession()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=MySql",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Rollback();

        var commands = CollectCommands(factory);

        Assert.Contains(commands,
            c => c.Contains("TRANSACTION READ WRITE", StringComparison.OrdinalIgnoreCase));
    }

    // ── MariaDB ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MariaDb_ReadOnlyTransaction_Commit_ResetsSession()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=MariaDb",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Commit();

        var commands = CollectCommands(factory);

        Assert.Contains(commands,
            c => c.Contains("TRANSACTION READ ONLY", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(commands,
            c => c.Contains("TRANSACTION READ WRITE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MariaDb_ReadOnlyTransaction_Rollback_ResetsSession()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=MariaDb",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Rollback();

        var commands = CollectCommands(factory);

        Assert.Contains(commands,
            c => c.Contains("TRANSACTION READ WRITE", StringComparison.OrdinalIgnoreCase));
    }
}
