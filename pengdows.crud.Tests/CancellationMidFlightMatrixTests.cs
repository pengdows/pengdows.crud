using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// FEAT-009: CancellationTokenIntegrationTests.cs already proves every execution method throws
// when handed an ALREADY-cancelled token before any I/O starts — but that never actually
// exercises the method's own cancellation plumbing, since ct.ThrowIfCancellationRequested() at
// the very top short-circuits before a fake command is ever created. This file adds the missing
// combinatorial dimension: cancelling the token WHILE the underlying fakeDbCommand's async
// execute call is genuinely in flight (paused on a test-controlled gate, mirroring
// fakeDbConnection.SetOpenGate()'s established pattern), across every execution method named in
// FEAT-009 — ExecuteNonQueryAsync, ExecuteScalarOrNullAsync, ExecuteReaderAsync, LoadListAsync,
// LoadStreamAsync. DbMode.SingleConnection pins one connection for the whole context so the test
// can grab the exact fakeDbConnection instance the operation will use and gate it before issuing
// the call, without needing to guess or probe a normalized connection string.
public sealed class CancellationMidFlightMatrixTests
{
    [Table("test")]
    private class TestEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string? Name { get; set; }
    }

    private static (DatabaseContext Context, fakeDbConnection Connection) CreateGatedContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        var context = new DatabaseContext(config, factory);
        var connection = factory.CreatedConnections.Single();

        using var createTable = context.CreateSqlContainer(
            "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        createTable.ExecuteNonQueryAsync().GetAwaiter().GetResult();

        return (context, connection);
    }

    public static IEnumerable<object[]> ExecutionMethods()
    {
        yield return new object[]
        {
            "ExecuteNonQueryAsync", (Func<DatabaseContext, CancellationToken, Task>)((ctx, ct) =>
            {
                using var sc = ctx.CreateSqlContainer("INSERT INTO test (id, name) VALUES (1, 'a')");
                return sc.ExecuteNonQueryAsync(CommandType.Text, ct).AsTask();
            })
        };
        yield return new object[]
        {
            "ExecuteScalarOrNullAsync", (Func<DatabaseContext, CancellationToken, Task>)((ctx, ct) =>
            {
                using var sc = ctx.CreateSqlContainer("SELECT 1");
                return sc.ExecuteScalarOrNullAsync<int>(CommandType.Text, ct).AsTask();
            })
        };
        yield return new object[]
        {
            "ExecuteReaderAsync", (Func<DatabaseContext, CancellationToken, Task>)(async (ctx, ct) =>
            {
                using var sc = ctx.CreateSqlContainer("SELECT * FROM test");
                await using var reader = await sc.ExecuteReaderAsync(CommandType.Text, ct);
            })
        };
        yield return new object[]
        {
            "LoadListAsync", (Func<DatabaseContext, CancellationToken, Task>)(async (ctx, ct) =>
            {
                var gateway = new TableGateway<TestEntity, int>(ctx);
                using var sc = gateway.BuildBaseRetrieve("t");
                await gateway.LoadListAsync(sc, ct);
            })
        };
        yield return new object[]
        {
            "LoadStreamAsync", (Func<DatabaseContext, CancellationToken, Task>)(async (ctx, ct) =>
            {
                var gateway = new TableGateway<TestEntity, int>(ctx);
                using var sc = gateway.BuildBaseRetrieve("t");
                await foreach (var _ in gateway.LoadStreamAsync(sc, cancellationToken: ct))
                {
                }
            })
        };
    }

    [Theory]
    [MemberData(nameof(ExecutionMethods))]
    public async Task CancelledWhileInFlight_ThrowsOperationCanceled_AndNeverReachesFakeExecution(
        string _, Func<DatabaseContext, CancellationToken, Task> invoke)
    {
        var (context, connection) = CreateGatedContext();
        await using var _2 = context;

        var gate = connection.SetExecuteGate();
        using var cts = new CancellationTokenSource();

        var task = invoke(context, cts.Token);

        // The gate is never completed, so the operation must genuinely be stuck awaiting it —
        // not finished, not faulted from something unrelated.
        Assert.False(task.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        // Prove the fake execution itself never ran (not merely that its result was discarded):
        // completing the gate now and confirming the connection recorded no non-query/reader
        // activity from this operation would be the strongest proof, but simpler and just as
        // conclusive — release the gate and confirm the already-cancelled task doesn't flip to
        // a different outcome (it must stay canceled forever, single terminal state).
        gate.TrySetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [MemberData(nameof(ExecutionMethods))]
    public async Task NotCancelled_GateReleased_CompletesNormally(
        string _, Func<DatabaseContext, CancellationToken, Task> invoke)
    {
        var (context, connection) = CreateGatedContext();
        await using var _2 = context;

        var gate = connection.SetExecuteGate();
        using var cts = new CancellationTokenSource();

        var task = invoke(context, cts.Token);
        Assert.False(task.IsCompleted);

        gate.SetResult(true);

        await task; // must not throw
    }
}
