using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class BeginTransactionAsyncTests
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

    [Fact]
    public async Task BeginTransactionAsync_CreatesUsableTransaction()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        await using var tx = await context.BeginTransactionAsync();

        Assert.NotNull(tx);
        Assert.False(tx.IsCompleted);
        await tx.CommitAsync();
        Assert.True(tx.WasCommitted);
    }

    [Fact]
    public async Task BeginTransactionAsync_ReadOnly_DuckDb_ExecutesReadOnlySql()
    {
        var factory = new RecordingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        await using var tx = await context.BeginTransactionAsync(readOnly: true);
        await tx.CommitAsync();

        var allCommands = new List<string>();
        foreach (var conn in factory.Connections)
        {
            allCommands.AddRange(conn.Commands);
        }

        // TryEnterReadOnlyTransactionAsync should have executed read-only SQL
        Assert.Contains(allCommands,
            c => c.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                 c.Contains("read_only", StringComparison.OrdinalIgnoreCase));

        // TryResetReadOnlySessionAsync should have executed read-write SQL
        Assert.Contains(allCommands,
            c => c.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                 c.Contains("read_write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BeginTransactionAsync_WithIsolationProfile()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=fake;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        await using var tx = await context.BeginTransactionAsync(IsolationProfile.StrictConsistency);

        Assert.NotNull(tx);
        Assert.False(tx.IsCompleted);
        await tx.CommitAsync();
        Assert.True(tx.WasCommitted);
    }

    [Fact]
    public async Task BeginTransactionAsync_OnTransactionContext_ThrowsForNesting()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        await using var tx = await context.BeginTransactionAsync();

        // Nested BeginTransactionAsync should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((IDatabaseContext)tx).BeginTransactionAsync());

        await tx.CommitAsync();
    }

    [Fact]
    public async Task BeginTransaction_Sync_StillWorks()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction();

        Assert.NotNull(tx);
        Assert.False(tx.IsCompleted);
        await tx.CommitAsync();
        Assert.True(tx.WasCommitted);
    }

    /// <summary>
    /// Sync BeginTransaction(readOnly: true) on DuckDB must execute the read-only session SQL
    /// via TryEnterReadOnlyTransaction → TryExecuteReadOnlySql (sync path).
    /// This verifies the sync-over-async path in TryExecuteReadOnlySql correctly sets
    /// session state without unnecessary Task.Run overhead.
    /// </summary>
    [Fact]
    public void BeginTransaction_Sync_ReadOnly_DuckDb_ExecutesReadOnlySessionSql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory);

        using var tx = context.BeginTransaction(readOnly: true);
        tx.Commit();

        // The read-only session SQL must have been sent via ExecuteSessionNonQuery.
        var allExecuted = factory.CreatedConnections
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .ToList();

        Assert.Contains(allExecuted,
            c => c.Contains("access_mode", StringComparison.OrdinalIgnoreCase) &&
                 c.Contains("read_only", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// BeginTransactionAsync on a TransactionContext must return a faulted Task,
    /// NOT throw synchronously. A synchronous throw from a Task-returning method
    /// bypasses async state-machine try/catch blocks in callers.
    /// </summary>
    [Fact]
    public async Task BeginTransactionAsync_IsolationLevel_OnTransactionContext_ReturnsFaultedTask_NotSynchronousThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        using var tx = context.BeginTransaction();

        // Call WITHOUT awaiting — just capture the returned Task.
        // A synchronous throw would be captured by Record.Exception.
        Task<ITransactionContext>? task = null;
        var syncEx = Record.Exception(() =>
        {
            task = ((IDatabaseContext)tx).BeginTransactionAsync();
        });

        // The exception must NOT have been thrown synchronously.
        Assert.Null(syncEx);
        Assert.NotNull(task);

        // The exception must be embedded in the Task (faulted).
        Assert.True(task!.IsFaulted, "Expected faulted Task, not synchronous throw");
        Assert.IsType<InvalidOperationException>(task.Exception!.InnerException);

        await tx.CommitAsync();
    }

    /// <summary>
    /// Same as above but for the IsolationProfile overload of BeginTransactionAsync.
    /// </summary>
    [Fact]
    public async Task BeginTransactionAsync_IsolationProfile_OnTransactionContext_ReturnsFaultedTask_NotSynchronousThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);
        using var tx = context.BeginTransaction();

        Task<ITransactionContext>? task = null;
        var syncEx = Record.Exception(() =>
        {
            task = ((IDatabaseContext)tx).BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
        });

        Assert.Null(syncEx);
        Assert.NotNull(task);
        Assert.True(task!.IsFaulted, "Expected faulted Task, not synchronous throw");
        Assert.IsType<InvalidOperationException>(task.Exception!.InnerException);

        await tx.CommitAsync();
    }
}