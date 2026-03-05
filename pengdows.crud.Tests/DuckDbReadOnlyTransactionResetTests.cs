using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DuckDbReadOnlyTransactionResetTests
{
    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> Commands { get; } = new();
        public List<string> ConnectionStrings { get; } = new();

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, Commands);
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
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

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }


    // DuckDB read-only is enforced via access_mode=READ_ONLY in the connection string when
    // using DbMode.SingleWriter or DbMode.SingleConnection (where a dedicated read connection
    // is created). In DbMode.Standard, BeginTransaction(readOnly: true) uses the standard
    // connection pool and relies on the DuckDB file/connection-level access control.
    // These tests verify that no SET access_mode session SQL is emitted (regression guard).

    [Fact]
    public async Task DuckDb_ReadOnlyTransaction_Commit_NoAccessModeSql()
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

        // No SET access_mode SQL should be emitted — enforcement is via connection string only
        var allCommands = factory.Connections.SelectMany(c => c.Commands).ToList();
        Assert.DoesNotContain(allCommands,
            c => c.Contains("SET access_mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuckDb_ReadOnlyTransaction_Rollback_NoAccessModeSql()
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

        // No SET access_mode SQL should be emitted — enforcement is via connection string only
        var allCommands = factory.Connections.SelectMany(c => c.Commands).ToList();
        Assert.DoesNotContain(allCommands,
            c => c.Contains("SET access_mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuckDb_ReadOnlyTransaction_CompletesCleanly_NoAccessModeSql()
    {
        // Previously, DuckDB emitted SET access_mode = 'read_only' / 'read_write' SQL.
        // Now enforcement is via connection string only. This test verifies no SQL
        // access_mode commands are emitted and the transaction completes without error.
        var factory = new RecordingFactory();
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

        var allCommands = factory.Connections.SelectMany(c => c.Commands).ToList();
        Assert.DoesNotContain(allCommands,
            c => c.Contains("SET access_mode", StringComparison.OrdinalIgnoreCase));
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