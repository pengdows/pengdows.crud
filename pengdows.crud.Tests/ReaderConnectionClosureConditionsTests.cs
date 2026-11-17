using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G4/§3 (CONCURRENCY.md): "Checkout behind an open reader... released when the reader is
/// disposed... Which call sites pass [shouldCloseConnection] true is an Assumption; Test:
/// pending." That framing undersold how traceable this already was: the whole decision is one
/// boolean, computed once, at one call site (SqlContainer.cs's ExecuteReaderAsyncInternal):
///
///   closesConnectionAfterRead = !(isTransaction || isSingleConnection ||
///                                  (PinnedConnection != null) || ReferenceEquals(conn, _connectionHold))
///
/// These tests pin each branch directly by observing the real CommandBehavior
/// (fakeDbConnection.ExecutedReaderBehaviors) ExecuteReaderAsyncInternal actually hands to the
/// provider -- CommandBehavior.CloseConnection present or absent -- rather than inferring it.
/// The fourth branch (PinnedConnection) is not re-proven here: it already follows from
/// GeneratedIdConnectionAffinityTests.CreateAsync_SingleWriter_FallbackRunsOnThePinnedConnectionWithoutASecondPermit
/// -- if that reader had closed its connection, the generated-id query could not have reused the
/// exact same physical connection object the INSERT used, which that test already confirms it does.
/// </summary>
public sealed class ReaderConnectionClosureConditionsTests
{
    [Fact(Timeout = 10000)]
    public async Task StandaloneRead_StandardMode_ClosesTheConnectionOnDispose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(config, factory);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("SELECT id FROM test");
        using var reader = await sc.ExecuteReaderAsync();

        var behavior = Assert.Single(conn.ExecutedReaderBehaviors);
        Assert.True(behavior.HasFlag(CommandBehavior.CloseConnection),
            "A standalone read outside a transaction, SingleConnection, or any pin must close its connection on dispose.");
    }

    [Fact(Timeout = 10000)]
    public async Task ReadInsideATransaction_DoesNotCloseTheConnectionOnDispose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(config, factory);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var txn = ctx.BeginTransaction();
        using var sc = txn.CreateSqlContainer("SELECT id FROM test");
        using var reader = await sc.ExecuteReaderAsync();

        var behavior = Assert.Single(conn.ExecutedReaderBehaviors);
        Assert.False(behavior.HasFlag(CommandBehavior.CloseConnection),
            "A read inside a transaction must not close the connection the transaction itself owns.");
    }

    [Fact(Timeout = 10000)]
    public async Task SingleConnectionMode_DoesNotCloseTheSharedConnectionOnDispose()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        using var ctx = new DatabaseContext(config, factory);

        var conn = factory.CreatedConnections.Single();
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });

        using (var sc = ctx.CreateSqlContainer("SELECT id FROM test"))
        {
            var reader = await sc.ExecuteReaderAsync();
            await reader.DisposeAsync();
        }

        // SingleConnection's one physical connection also runs internal probe queries during
        // DatabaseContext construction, so this call's own behavior isn't necessarily the only
        // (or the first) entry -- check the one it actually just added, not the whole history.
        var behavior = conn.ExecutedReaderBehaviors.Last();
        Assert.False(behavior.HasFlag(CommandBehavior.CloseConnection),
            "SingleConnection's one shared connection must never be closed by an individual reader's dispose.");

        // Decisive, not just flag-based: the connection must still be usable for a second read
        // once the first reader is disposed (SingleConnection's own gate still serializes
        // ordinary commands against each other; this isn't a claim about running concurrently).
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 2 } });
        using var sc2 = ctx.CreateSqlContainer("SELECT id FROM test");
        using var reader2 = await sc2.ExecuteReaderAsync();
        Assert.NotNull(reader2);
    }
}
