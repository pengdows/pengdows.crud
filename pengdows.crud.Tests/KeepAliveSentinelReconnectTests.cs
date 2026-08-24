#region

using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// KeepAlive's sentinel connection exists purely to keep an embedded/local database engine
/// loaded — the sentinel consuming a writer-governor slot for the context's whole lifetime is
/// intentional (that's the mechanism). The gap this covers: nothing previously detected or
/// repaired a sentinel that unexpectedly transitioned to Broken/Closed for a reason other than
/// context disposal, silently breaking the "keep the engine loaded" guarantee and leaking its
/// pool-governor slot forever (TrackedConnection only releases a slot on Dispose()).
///
/// Contract: an unexpectedly lost sentinel is repaired before the next connection-requiring
/// operation — not continuously/instantly, since detection is necessarily lazy (checked at the
/// top of GetConnection, no background monitor).
/// </summary>
public class KeepAliveSentinelReconnectTests
{
    // LocalDB-style SqlServer connection string preserves KeepAlive mode (plain SqlServer
    // coerces KeepAlive to Standard) — same pattern as
    // ConnectionStrategyTests.KeepAliveRequested_LocalDb_RetainsKeepAliveSentinelConnection.
    private static DatabaseContext CreateKeepAliveContext(fakeDbFactory factory)
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(cfg, factory);
    }

    private static fakeDbConnection Unwrap(ITrackedConnection tracked)
    {
        return (fakeDbConnection)((IInternalConnectionWrapper)tracked).UnderlyingConnection;
    }

    [Fact]
    public void GetConnection_SentinelBroken_TransparentlyReconnectsBeforeNextOperation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = CreateKeepAliveContext(factory);
        Assert.Equal(DbMode.KeepAlive, ctx.ConnectionMode);

        var originalSentinel = ctx.PersistentConnection!;
        Unwrap(originalSentinel).BreakConnection();
        Assert.Equal(ConnectionState.Broken, originalSentinel.State);

        var opConnection = ctx.GetConnection(ExecutionType.Read);

        var newSentinel = ctx.PersistentConnection;
        Assert.NotSame(originalSentinel, newSentinel);
        Assert.NotNull(newSentinel);
        Assert.Equal(ConnectionState.Open, newSentinel!.State);

        ctx.CloseAndDisposeConnection(opConnection);
    }

    [Fact]
    public void GetConnection_SentinelHealthy_DoesNotReconnect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = CreateKeepAliveContext(factory);

        var originalSentinel = ctx.PersistentConnection;
        Assert.NotNull(originalSentinel);

        var opConnection = ctx.GetConnection(ExecutionType.Read);

        Assert.Same(originalSentinel, ctx.PersistentConnection);

        ctx.CloseAndDisposeConnection(opConnection);
    }

    [Fact]
    public async Task GetConnection_ConcurrentCallsRaceBrokenSentinel_AllObserveSameRepairedSentinel()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = CreateKeepAliveContext(factory);

        var originalSentinel = ctx.PersistentConnection!;
        Unwrap(originalSentinel).BreakConnection();

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => ctx.GetConnection(ExecutionType.Read)))
            .ToArray();
        var opConnections = await Task.WhenAll(tasks);

        // No double-repair: every caller observes the exact same replacement sentinel, and it
        // is healthy.
        var repairedSentinel = ctx.PersistentConnection;
        Assert.NotSame(originalSentinel, repairedSentinel);
        Assert.Equal(ConnectionState.Open, repairedSentinel!.State);

        foreach (var conn in opConnections)
        {
            ctx.CloseAndDisposeConnection(conn);
        }
    }
}
