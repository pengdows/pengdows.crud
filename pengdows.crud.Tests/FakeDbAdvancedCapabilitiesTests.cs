using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// Extends fakeDb to close gaps surfaced while pushing pengdows.stormgate's EF-Core-over-fakeDb
// testing further: capturing real parameter values at execution time, simulating a reader that
// fails mid-enumeration, tracking transaction commit/rollback invocation, and giving OpenAsync a
// deterministic, test-controlled gate for genuine concurrency proofs.
public class fakeDbAdvancedCapabilitiesTests
{
    [Fact]
    public void ExecuteNonQuery_CapturesParameterNameAndValue_AtExecutionTime()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Customers\" SET \"Name\" = @p0 WHERE \"Id\" = @p1";
        var p0 = cmd.CreateParameter();
        p0.ParameterName = "@p0";
        p0.Value = "Ada";
        cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter();
        p1.ParameterName = "@p1";
        p1.Value = 42;
        cmd.Parameters.Add(p1);

        cmd.ExecuteNonQuery();

        var captured = Assert.Single(conn.ExecutedNonQueryCommands);
        Assert.Equal(cmd.CommandText, captured.CommandText);
        Assert.Collection(
            captured.Parameters,
            p => { Assert.Equal("@p0", p.Name); Assert.Equal("Ada", p.Value); },
            p => { Assert.Equal("@p1", p.Name); Assert.Equal(42, p.Value); });
    }

    [Fact]
    public async Task ExecuteReaderAsync_CapturesParameterValues_BeforeCommandDisposal()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();
        conn.EnqueueReaderResult(new[] { new System.Collections.Generic.Dictionary<string, object?> { ["Id"] = 1 } });

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT \"Id\" FROM \"Customers\" WHERE \"Name\" = @name";
            var p = cmd.CreateParameter();
            p.ParameterName = "@name";
            p.Value = "Ada";
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync();
        }

        var captured = Assert.Single(conn.ExecutedReaderCommands);
        var boundValue = Assert.Single(captured.Parameters);
        Assert.Equal("@name", boundValue.Name);
        Assert.Equal("Ada", boundValue.Value);
    }

    [Fact]
    public async Task Reader_ThrowsConfiguredException_AfterConfiguredNumberOfSuccessfulReads()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        var failure = new InvalidOperationException("simulated mid-stream failure");
        conn.EnqueueReaderResult(
            new[]
            {
                new System.Collections.Generic.Dictionary<string, object?> { ["Id"] = 1 },
                new System.Collections.Generic.Dictionary<string, object?> { ["Id"] = 2 }
            },
            failAfterRowCount: 1,
            failure);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Id\" FROM \"Customers\"";
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync());
        Assert.Same(failure, thrown);
    }

    [Fact]
    public void Transaction_TracksCommitAndRollbackCallCounts()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        using var txn = (fakeDbTransaction)conn.BeginTransaction();
        Assert.Equal(0, txn.CommitCallCount);
        txn.Commit();
        Assert.Equal(1, txn.CommitCallCount);
        Assert.Equal(0, txn.RollbackCallCount);

        using var txn2 = (fakeDbTransaction)conn.BeginTransaction();
        txn2.Rollback();
        Assert.Equal(1, txn2.RollbackCallCount);
    }

    [Fact]
    public async Task OpenAsync_WaitsForGate_UntilTestReleasesIt()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        var gate = conn.SetOpenGate();

        var openTask = conn.OpenAsync();
        Assert.False(openTask.IsCompleted);
        Assert.Equal(ConnectionState.Closed, conn.State);

        gate.SetResult(true);
        await openTask;

        Assert.Equal(ConnectionState.Open, conn.State);
    }

    // Code-review finding: fakeDbCommand.ExecuteScalarAsync checked TryGetAsyncOnlyScalarFailure
    // before consulting the connection's execute gate, so a connection configured with both an
    // async-only scalar failure and an execute gate bypassed the gate entirely and faulted
    // immediately instead of blocking in flight until the gate was released or cancelled.
    [Fact]
    public async Task ExecuteScalarAsync_WithGateAndAsyncOnlyFailure_WaitsForGateBeforeFaulting()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        const string commandText = "SELECT 1";
        var configuredFailure = new InvalidOperationException("simulated async-only scalar failure");
        conn.SetAsyncOnlyScalarFailure(commandText, configuredFailure);
        var gate = conn.SetExecuteGate();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        var scalarTask = cmd.ExecuteScalarAsync();

        Assert.False(scalarTask.IsCompleted);

        gate.SetResult(true);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => scalarTask);
        Assert.Same(configuredFailure, thrown);
    }

    private sealed class NamedFakeCommand : fakeDbCommand
    {
        public NamedFakeCommand(fakeDbConnection connection) : base(connection)
        {
        }

        public bool WasConstructedByFactory { get; set; }
    }

    [Fact]
    public void CommandFactory_Set_ConstructsCommandsThroughIt_InsteadOfPlainFakeDbCommand()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.CommandFactory = c => new NamedFakeCommand(c) { WasConstructedByFactory = true };
        conn.Open();

        using var cmd = conn.CreateCommand();

        var named = Assert.IsType<NamedFakeCommand>(cmd);
        Assert.True(named.WasConstructedByFactory);
    }

    [Fact]
    public void CommandFactory_SetOnFactory_PropagatesToEveryCreatedConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle)
        {
            CommandFactory = c => new NamedFakeCommand(c) { WasConstructedByFactory = true }
        };

        using var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = "Data Source=test;EmulatedProduct=Oracle";
        conn.Open();
        using var cmd = conn.CreateCommand();

        Assert.IsType<NamedFakeCommand>(cmd);
    }

    [Fact]
    public async Task Reader_HonorsRealCancellationToken_PassedIntoReadAsync()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        using var cts = new CancellationTokenSource();
        conn.EnqueueReaderResult(
            new[]
            {
                new System.Collections.Generic.Dictionary<string, object?> { ["Id"] = 1 },
                new System.Collections.Generic.Dictionary<string, object?> { ["Id"] = 2 }
            },
            cancelAfterRowCount: 1,
            cts);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Id\" FROM \"Customers\"";
        await using var reader = await cmd.ExecuteReaderAsync(cts.Token);

        Assert.True(await reader.ReadAsync(cts.Token));
        Assert.False(cts.IsCancellationRequested);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_RetriesThroughQueuedTransientFailures_ThenSucceeds()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        var failure1 = new InvalidOperationException("transient #1");
        var failure2 = new InvalidOperationException("transient #2");
        conn.EnqueueTransientNonQueryFailures(failure1, failure2);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Customers\" SET \"Name\" = 'Ada'";

        var firstAttempt = await Assert.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Same(failure1, firstAttempt);

        var secondAttempt = await Assert.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Same(failure2, secondAttempt);

        var thirdAttempt = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, thirdAttempt);
    }

    [Fact]
    public async Task Reader_ReportsOverriddenRecordsAffected()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        // Some EF Core providers (e.g. Snowflake's SnowflakeModificationCommandBatch) determine
        // SaveChanges rows-affected by reading DbDataReader.RecordsAffected directly, rather than
        // reading a row/column value the way SQLite's/SQL Server's provider-generated
        // "SELECT changes()" pattern does. RecordsAffected otherwise defaults to 0 (ADO.NET's
        // convention for a reader with no applicable affected-row count), which made every such
        // provider see "0 rows affected" regardless of what was queued via the rows-only overload.
        conn.EnqueueReaderResult(Array.Empty<System.Collections.Generic.Dictionary<string, object?>>(), recordsAffected: 1);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Customers\" SET \"Name\" = 'Ada'";
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.Equal(1, reader.RecordsAffected);
    }

    [Fact]
    public async Task Reader_RecordsAffectedAccess_ThrowsConfiguredException()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        // A provider whose rows-affected check reads DbDataReader.RecordsAffected directly (see
        // Reader_ReportsOverriddenRecordsAffected above) never calls Read()/ReadAsync() at all —
        // so a canned exception on FailAfterReadCount/FailException (which only fires from
        // Read()) can never reach it. Simulating "the provider itself threw" for such a provider
        // requires the RecordsAffected property getter itself to be able to throw.
        var failure = new InvalidOperationException("simulated provider failure");
        conn.EnqueueReaderResult(Array.Empty<System.Collections.Generic.Dictionary<string, object?>>(), failure);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Customers\" SET \"Name\" = 'Ada'";
        await using var reader = await cmd.ExecuteReaderAsync();

        var thrown = Assert.Throws<InvalidOperationException>(() => reader.RecordsAffected);
        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task EnqueueReaderResult_AcceptsAPreconfiguredReader_CombiningBothFailureMechanisms()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        // A single reader that fails whether the caller reads a row (FailException, e.g.
        // SQLite's/SQL Server's "SELECT changes()" pattern) or reads RecordsAffected directly
        // (RecordsAffectedException, e.g. Snowflake) — lets a test inject one provider failure
        // that's genuinely path-agnostic across every confirmed rows-affected mechanism, without
        // needing to know in advance which one a given provider actually uses.
        var failure = new InvalidOperationException("simulated provider failure");
        var reader = new fakeDbDataReader(Array.Empty<System.Collections.Generic.Dictionary<string, object>>())
        {
            FailAfterReadCount = 0,
            FailException = failure,
            RecordsAffectedException = failure
        };
        conn.EnqueueReaderResult(reader);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE \"Customers\" SET \"Name\" = 'Ada'";
        await using var executedReader = await cmd.ExecuteReaderAsync();

        var thrownFromRecordsAffected = Assert.Throws<InvalidOperationException>(() => executedReader.RecordsAffected);
        Assert.Same(failure, thrownFromRecordsAffected);
    }
}
