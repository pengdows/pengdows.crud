using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
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
        private bool _thrown;

        public RecordingPrepareCommand(fakeDbConnection connection) : base(connection)
        {
        }

        public override void Prepare()
        {
            PrepareAttempts++;
            if (ThrowOnFirstPrepare && !_thrown)
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

        protected override DbCommand CreateDbCommand()
        {
            var cmd = new RecordingPrepareCommand(this);
            if (ThrowOnNextPrepare)
            {
                cmd.ThrowOnFirstPrepare = true;
                ThrowOnNextPrepare = false;
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
        ReadWriteMode mode = ReadWriteMode.ReadWrite, bool? forcePrepare = null, bool? disablePrepare = null)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            DbMode = DbMode.Standard,
            ReadWriteMode = mode,
            ForceManualPrepare = forcePrepare,
            DisablePrepare = disablePrepare
        };
        return new DatabaseContext(cfg, factory);
    }

    [Fact]
    public async Task Prepare_CallsOncePerText_ThenCachesForConnection()
    {
        var factory = new RecordingPrepareFactory(SupportedDatabase.PostgreSql);
        await using var ctx = CreateContext(factory, forcePrepare: true);

        await using var tx = ctx.BeginTransaction(readOnly: false);
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
        await using var ctx = CreateContext(factory, disablePrepare: true);

        await using var tx = ctx.BeginTransaction(readOnly: false);
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
            ForceManualPrepare = true
        };
        await using var ctx = new DatabaseContext(cfg, factory);

        await using var tx = ctx.BeginTransaction(readOnly: false);
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
        await using var ctx = CreateContext(factory, forcePrepare: true);

        await using var tx = ctx.BeginTransaction(readOnly: false);

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
}