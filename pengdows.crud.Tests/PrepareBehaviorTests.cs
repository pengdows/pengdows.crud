using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class PrepareBehaviorTests
{
    private sealed class RecordingPrepareCommand : fakeDbCommand
    {
        public int PrepareAttempts { get; private set; }
        public int PrepareSuccesses { get; private set; }
        public bool ThrowOnFirstPrepare { get; set; }
        public Exception? ThrowOnFirstPrepareException { get; set; }
        private bool _thrown;

        public RecordingPrepareCommand(fakeDbConnection connection) : base(connection)
        {
        }

        public override void Prepare()
        {
            PrepareAttempts++;
            if (!_thrown && ThrowOnFirstPrepareException != null)
            {
                _thrown = true;
                throw ThrowOnFirstPrepareException;
            }

            if (!_thrown && ThrowOnFirstPrepare)
            {
                _thrown = true;
                throw new NotSupportedException("Simulated provider does not support Prepare()");
            }

            PrepareSuccesses++;
        }
    }

    private sealed class RecordingPrepareConnection : fakeDbConnection
    {
        public List<RecordingPrepareCommand> Commands { get; } = new();
        public bool ThrowOnNextPrepare { get; set; }
        public Exception? ThrowOnNextPrepareException { get; set; }

        protected override DbCommand CreateDbCommand()
        {
            var cmd = new RecordingPrepareCommand(this);
            if (ThrowOnNextPrepare)
            {
                cmd.ThrowOnFirstPrepare = true;
                ThrowOnNextPrepare = false;
            }

            if (ThrowOnNextPrepareException != null)
            {
                cmd.ThrowOnFirstPrepareException = ThrowOnNextPrepareException;
                ThrowOnNextPrepareException = null;
            }

            Commands.Add(cmd);
            return cmd;
        }
    }

    private sealed class RecordingPrepareFactory : DbProviderFactory
    {
        private readonly SupportedDatabase _db;
        public List<RecordingPrepareConnection> Connections { get; } = new();

        public RecordingPrepareFactory(SupportedDatabase db)
        {
            _db = db;
        }

        public override DbConnection CreateConnection()
        {
            var conn = new RecordingPrepareConnection
            {
                ConnectionString = $"Data Source=test;EmulatedProduct={_db}"
            };
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

    private static DatabaseContext CreateContext(RecordingPrepareFactory factory,
        ReadWriteMode mode = ReadWriteMode.ReadWrite, CommandPrepareMode prepareMode = CommandPrepareMode.Auto)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            DbMode = DbMode.Standard,
            ReadWriteMode = mode,
            PrepareMode = prepareMode,
            
        };
        return new DatabaseContext(cfg, factory);
    }

    [Fact]
    public async Task Prepare_CallsOncePerText_ThenCachesForConnection()
    {
        var factory = new RecordingPrepareFactory(SupportedDatabase.PostgreSql);
        await using var ctx = CreateContext(factory, prepareMode: CommandPrepareMode.Always);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);
        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 1);
            _ = await sc.ExecuteNonQueryAsync();
        }

        // Same text in same transaction (same connection) should not re-prepare
        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 2);
            _ = await sc.ExecuteNonQueryAsync();
        }

        // Different text should prepare again
        await using (var sc = tx.CreateSqlContainer("SELECT @p0, @p1") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.String, "x");
            sc!.AddParameterWithValue("p1", DbType.Int32, 7);
            _ = await sc.ExecuteNonQueryAsync();
        }

        // Assert: one connection, three commands created; prepare attempts 2 (first + text change)
        var totalAttempts = 0;
        var totalSuccess = 0;
        foreach (var connection in factory.Connections)
        foreach (var c in connection.Commands)
        {
            totalAttempts += c.PrepareAttempts;
            totalSuccess += c.PrepareSuccesses;
        }

        Assert.Equal(2, totalAttempts);
        Assert.Equal(2, totalSuccess);
    }

    [Fact]
    public async Task Prepare_DisabledGlobally_SkipsPrepare()
    {
        var factory = new RecordingPrepareFactory(SupportedDatabase.PostgreSql);
        await using var ctx = CreateContext(factory, prepareMode: CommandPrepareMode.Never);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);
        await using var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer;
        sc!.AddParameterWithValue("p0", DbType.Int32, 1);
        _ = await sc.ExecuteNonQueryAsync();

        var attempts = 0;
        foreach (var connection in factory.Connections)
        foreach (var c in connection.Commands)
        {
            attempts += c.PrepareAttempts;
        }

        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task Prepare_ForcedOn_ForDialectThatDefaultsOff()
    {
        var factory = new RecordingPrepareFactory(SupportedDatabase.SqlServer);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.SqlServer}",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PrepareMode = CommandPrepareMode.Always
        };
        await using var ctx = new DatabaseContext(cfg, factory);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);
        await using var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer;
        sc!.AddParameterWithValue("p0", DbType.Int32, 1);
        _ = await sc.ExecuteNonQueryAsync();

        var attempts = 0;
        foreach (var connection in factory.Connections)
        foreach (var c in connection.Commands)
        {
            attempts += c.PrepareAttempts;
        }

        Assert.True(attempts >= 1);
    }

    [Fact]
    public async Task Prepare_Failure_DisablesSubsequentPrepareAttempts()
    {
        var factory = new RecordingPrepareFactory(SupportedDatabase.PostgreSql);
        await using var ctx = CreateContext(factory, prepareMode: CommandPrepareMode.Always);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);

        // First command throws from Prepare()
        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 1);
            // Configure the next command on this connection to throw during Prepare()
            factory.Connections[0].ThrowOnNextPrepare = true;
            _ = await sc.ExecuteNonQueryAsync();
        }

        // Second command should not attempt prepare because dialect disables it on NotSupportedException
        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 2);
            _ = await sc.ExecuteNonQueryAsync();
        }

        var attempts = 0;
        foreach (var connection in factory.Connections)
        foreach (var c in connection.Commands)
        {
            attempts += c.PrepareAttempts;
        }

        Assert.Equal(1, attempts); // Only the first command attempted to prepare
    }

    [Fact]
    public async Task Prepare_MySqlMaxPreparedFailure_DisablesSubsequentPrepareAttemptsAcrossConnections()
    {
        var factory = new RecordingPrepareFactory(SupportedDatabase.MySql);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PrepareMode = CommandPrepareMode.Always  // MySQL defaults to OFF; opt-in required to test degradation
        };
        await using var ctx = new DatabaseContext(cfg, factory);

        await using (var tx1 = ctx.BeginTransaction(executionType: ExecutionType.Write))
        {
            factory.Connections[^1].ThrowOnNextPrepareException = new FakeMySqlDbException(
                1461,
                "Can't create more than max_prepared_stmt_count statements (current value: 16382)");

            await using var sc = tx1.CreateSqlContainer("SELECT @p0") as SqlContainer;
            sc!.AddParameterWithValue("p0", DbType.Int32, 1);
            _ = await sc.ExecuteNonQueryAsync();
        }

        await using (var tx2 = ctx.BeginTransaction(executionType: ExecutionType.Write))
        {
            await using var sc = tx2.CreateSqlContainer("SELECT @p0") as SqlContainer;
            sc!.AddParameterWithValue("p0", DbType.Int32, 2);
            _ = await sc.ExecuteNonQueryAsync();
        }

        var attempts = 0;
        foreach (var connection in factory.Connections)
        foreach (var command in connection.Commands)
        {
            attempts += command.PrepareAttempts;
        }

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Prepare_MySqlNonLimitFailure_DoesNotDisablePrepare()
    {
        var factory = new RecordingPrepareFactory(SupportedDatabase.MySql);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PrepareMode = CommandPrepareMode.Always  // MySQL defaults to OFF; opt-in required to test this path
        };
        await using var ctx = new DatabaseContext(cfg, factory);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);
        factory.Connections[^1].ThrowOnNextPrepareException = new FakeMySqlDbException(1205, "Lock wait timeout exceeded");

        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 1);
            var ex = await Assert.ThrowsAsync<CommandTimeoutException>(async () =>
            {
                _ = await sc.ExecuteNonQueryAsync();
            });
            Assert.IsType<FakeMySqlDbException>(ex.InnerException);
        }

        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 2);
            _ = await sc.ExecuteNonQueryAsync();
        }

        var attempts = 0;
        foreach (var connection in factory.Connections)
        foreach (var command in connection.Commands)
        {
            attempts += command.PrepareAttempts;
        }

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task MySql_DefaultPrepare_IsDisabled()
    {
        // MySQL defaults to PrepareStatements = false to avoid max_prepared_stmt_count exhaustion.
        // No ForceManualPrepare — just the dialect default.
        var factory = new RecordingPrepareFactory(SupportedDatabase.MySql);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var ctx = new DatabaseContext(cfg, factory);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);
        await using var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer;
        sc!.AddParameterWithValue("p0", DbType.Int32, 1);
        _ = await sc.ExecuteNonQueryAsync();

        var attempts = factory.Connections.Sum(c => c.Commands.Sum(cmd => cmd.PrepareAttempts));
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task Auto_PostgreSql_DefaultPrepare_IsEnabled()
    {
        // PostgreSQL defaults to PrepareStatements = true.
        // Auto mode must read the dialect recommendation, not hard-code a value.
        var factory = new RecordingPrepareFactory(SupportedDatabase.PostgreSql);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PrepareMode = CommandPrepareMode.Auto
        };
        await using var ctx = new DatabaseContext(cfg, factory);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);
        await using var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer;
        sc!.AddParameterWithValue("p0", DbType.Int32, 1);
        _ = await sc.ExecuteNonQueryAsync();

        var attempts = factory.Connections.Sum(c => c.Commands.Sum(cmd => cmd.PrepareAttempts));
        Assert.True(attempts >= 1, "Auto mode for PostgreSQL should prepare (dialect default is true)");
    }

    [Fact]
    public async Task Auto_Failure_DisablesPrepareForRemainingCommands()
    {
        // Auto mode with a dialect that defaults to prepare-on; then a NotSupportedException
        // from Prepare() should disable further prepare attempts — same as Always mode.
        var factory = new RecordingPrepareFactory(SupportedDatabase.PostgreSql);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            PrepareMode = CommandPrepareMode.Auto
        };
        await using var ctx = new DatabaseContext(cfg, factory);

        await using var tx = ctx.BeginTransaction(executionType: ExecutionType.Write);

        factory.Connections[0].ThrowOnNextPrepare = true;
        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 1);
            _ = await sc.ExecuteNonQueryAsync();
        }

        // After the prepare failure the dialect should be exhausted; no further attempts.
        await using (var sc = tx.CreateSqlContainer("SELECT @p0") as SqlContainer)
        {
            sc!.AddParameterWithValue("p0", DbType.Int32, 2);
            _ = await sc.ExecuteNonQueryAsync();
        }

        var attempts = factory.Connections.Sum(c => c.Commands.Sum(cmd => cmd.PrepareAttempts));
        Assert.Equal(1, attempts);
    }

    private sealed class FakeMySqlDbException : DbException
    {
        public FakeMySqlDbException(int number, string message)
            : base(message)
        {
            Number = number;
        }

        public int Number { get; }
    }
}
