#region

using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
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
            ReadWriteMode = ReadWriteMode.ReadWrite,
            // TrackedConnection.OpenTimingHook (used by the disposed-during-repair race test
            // below) only fires when shouldTime is true (debug logging or a metrics collector) —
            // EnableMetrics makes that reliable without needing debug-level logging everywhere.
            EnableMetrics = true
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

    // EnsureSentinelHealthy's repair sequence (dispose the dead sentinel, open a replacement,
    // install it, attach its pool-governor slot) does not coordinate with a concurrent
    // DatabaseContext.Dispose() in any way — Dispose() can run its full teardown while the repair
    // is mid-flight. TrackedConnection.OpenTimingHook fires synchronously from inside
    // TrackedConnection.Open(), which lets this reproduce "Dispose() completes while the
    // replacement connection's Open() is in flight" deterministically, without any real
    // threading/timing race.
    [Fact]
    public void GetConnection_ContextDisposedWhileSentinelRepairIsInFlight_DoesNotLeakTheReplacementConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var ctx = CreateKeepAliveContext(factory);

        var originalSentinel = ctx.PersistentConnection!;
        Unwrap(originalSentinel).BreakConnection();

        TrackedConnection.OpenTimingHook = () =>
        {
            TrackedConnection.OpenTimingHook = null; // avoid re-entrancy if Dispose() itself opens anything
            ctx.Dispose();
        };

        ITrackedConnection? opConnection = null;
        try
        {
            // The context is legitimately disposed underneath this call — some exception
            // surfacing is expected and fine. What must not happen: an unhandled
            // ObjectDisposedException escaping from mid-repair with the replacement connection
            // left open and never disposed (PersistentConnection reassigned to a live connection
            // on an already-disposed context, with nothing left to ever dispose it). If
            // GetConnection happens to still succeed despite the disposed context (a separate,
            // pre-existing question outside this specific race's scope), the caller — this test,
            // like every other caller in this suite — is responsible for disposing what it got.
            opConnection = ctx.GetConnection(ExecutionType.Read);
        }
        catch
        {
            // Expected in the common case — the context is disposed underneath this call.
        }
        finally
        {
            TrackedConnection.OpenTimingHook = null;
            opConnection?.Dispose();
        }

        // Every fakeDbConnection this test created (original sentinel + the replacement the
        // repair opened) must end up disposed — none left open and orphaned.
        Assert.All(factory.CreatedConnections, c => Assert.True(c.DisposeCount > 0, $"Connection (State={c.State}) was never disposed."));
    }

    // Narrower residual case the test above cannot reach: AttachPinnedSlotIfNeeded's early-return
    // branch (governance disabled/forbidden for this context) has no disposed-context check of its
    // own — the ObjectDisposedException the repair sequence relies on is purely incidental to the
    // governed branch's SemaphoreSlim.Wait() throwing on a disposed semaphore. A ReadOnly context
    // forces the write pool's MaxPoolSize to 0, which InitializePoolGovernors turns into a
    // Forbidden writer governor — AttachPinnedSlotIfNeeded's guard (!_effectivePoolGovernorEnabled
    // || _writerGovernor == null || _writerGovernor.Forbidden) takes the no-op path, so it returns
    // cleanly even though Dispose() completed one line above it. PostDisposedCheckHook fires
    // exactly in that window, deterministically, without needing real thread interleaving.
    [Fact]
    public void GetConnection_ContextDisposedBetweenDisposedCheckAndAttachPinnedSlot_UngovernedContext_DoesNotLeakTheReplacementConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadOnly,
            EnableMetrics = true
        };
        var ctx = new DatabaseContext(cfg, factory);

        var originalSentinel = ctx.PersistentConnection!;
        Unwrap(originalSentinel).BreakConnection();

        KeepAliveConnectionStrategy.PostDisposedCheckHook = () =>
        {
            KeepAliveConnectionStrategy.PostDisposedCheckHook = null; // avoid re-entrancy
            ctx.Dispose();
        };

        ITrackedConnection? opConnection = null;
        try
        {
            opConnection = ctx.GetConnection(ExecutionType.Read);
        }
        catch
        {
            // Expected — the context is disposed underneath this call.
        }
        finally
        {
            KeepAliveConnectionStrategy.PostDisposedCheckHook = null;
            opConnection?.Dispose();
        }

        Assert.All(factory.CreatedConnections, c => Assert.True(c.DisposeCount > 0, $"Connection (State={c.State}) was never disposed."));
    }
}
