using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using pengdows.stormgate;

[assembly: InternalsVisibleTo("pengdows.stormgate.EntityFrameworkCore.Tests")]

namespace pengdows.stormgate.EntityFrameworkCore;

/// <summary>
/// Limits concurrent Entity Framework Core connection opens by hooking EF Core's
/// <see cref="DbConnectionInterceptor"/> extension point. Unlike wrapping the connection
/// (as <c>pengdows.stormgate.StormGate</c> does for raw ADO.NET consumers), this composes
/// with <c>AddDbContext</c>, <c>AddDbContextPool</c>, and <c>IDbContextFactory</c> alike,
/// because it fires on every real physical connection open/close regardless of how the
/// owning <see cref="Microsoft.EntityFrameworkCore.DbContext"/> instance was created or pooled.
/// </summary>
/// <remarks>
/// Consumes admission permits from a shared <see cref="StormGate"/> instance rather than
/// owning an independent semaphore — this is what makes "one database, one admission budget"
/// actually true when the same database is accessed through both EF Core and raw ADO.NET
/// (e.g. Dapper) in the same process: pass the SAME <see cref="StormGate"/> to both. A single
/// interceptor instance must still be shared across every <c>DbContextOptionsBuilder</c> it
/// gates — see
/// <see cref="StormGateDbContextOptionsBuilderExtensions.UseStormGate(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, StormGateConnectionInterceptor)"/>.
/// Constructing a fresh interceptor per <c>DbContext</c> is harmless on its own (it still shares
/// the underlying <see cref="StormGate"/>'s budget), but is pointless allocation — prefer one
/// shared interceptor instance, e.g. a singleton registered in DI.
/// </remarks>
public sealed class StormGateConnectionInterceptor : DbConnectionInterceptor
{
    private readonly StormGate _stormGate;

    // EF Core fires ConnectionFailed(Async) for ANY exception during an open attempt —
    // including a TimeoutException the shared StormGate itself threw from AcquirePermit(Async)
    // because the gate was saturated, in which case no permit was ever acquired. Tracking which
    // specific DbConnection instances actually hold a permit lets Release only fire for
    // attempts that got one, instead of unconditionally over-releasing on every failure.
    private readonly ConditionalWeakTable<DbConnection, PermitBox> _heldPermits = new();
    private readonly object _permitLock = new();

    public StormGateConnectionInterceptor(StormGate stormGate)
    {
        _stormGate = stormGate ?? throw new ArgumentNullException(nameof(stormGate));
    }

    private sealed class PermitBox(StormGate.StormGatePermit permit)
    {
        public StormGate.StormGatePermit Permit { get; } = permit;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        AcquirePermit(connection);
        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        await AcquirePermitAsync(connection, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // A permit acquired in ConnectionOpening(Async) but whose physical open then throws
    // must still be released — otherwise a single failed connection attempt permanently
    // shrinks the gate's capacity. Release only if THIS connection actually holds a permit —
    // see the _heldPermits comment above for why that check is required.
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        ReleasePermit(connection);
    }

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ReleasePermit(connection);

        return Task.CompletedTask;
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
    {
        ReleasePermit(connection);
    }

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        ReleasePermit(connection);

        return Task.CompletedTask;
    }

    public override void ConnectionDisposed(DbConnection connection, ConnectionEndEventData eventData)
    {
        ReleasePermit(connection);
    }

    public override Task ConnectionDisposedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        ReleasePermit(connection);
        return Task.CompletedTask;
    }

#if NET10_0_OR_GREATER
    public override void ConnectionCanceled(DbConnection connection, ConnectionEndEventData eventData)
    {
        ReleasePermit(connection);
    }

    public override Task ConnectionCanceledAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ReleasePermit(connection);
        return Task.CompletedTask;
    }
#endif

    // internal (not private) so StormGateConnectionInterceptorFakeDbTests can drive the exact
    // ConnectionOpening-fires-twice-for-an-already-tracked-connection sequence directly — going
    // through a real DbContext can't reproduce it deterministically, since EF Core's own
    // RelationalConnection checks the connection's ADO.NET State before deciding whether to
    // physically reopen it at all, short-circuiting before ConnectionOpening fires a second time.
    internal void AcquirePermit(DbConnection connection)
    {
        ReleaseStalePermit(connection);

        // StormGate.AcquirePermit already logs saturation and throws the same TimeoutException
        // on failure — nothing acquired, nothing to track.
        var permit = _stormGate.AcquirePermit();
        MarkPermitHeld(connection, permit);
    }

    private async ValueTask AcquirePermitAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        ReleaseStalePermit(connection);

        var permit = await _stormGate.AcquirePermitAsync(cancellationToken).ConfigureAwait(false);
        MarkPermitHeld(connection, permit);
    }

    private void MarkPermitHeld(DbConnection connection, StormGate.StormGatePermit permit)
    {
        lock (_permitLock)
        {
            // ConnectionOpening can fire again for a connection already tracked as holding a
            // permit — e.g. a redundant Open() call while the connection's ADO.NET state hasn't
            // yet transitioned to Closed/Broken, so ReleaseStalePermit above was a no-op. Without
            // this, AddOrUpdate would silently overwrite the tracked PermitBox, orphaning the
            // permit it replaces with nothing left to dispose it — a permanent, one-slot leak
            // from the shared admission budget per occurrence. Release what's being replaced
            // first, and only subscribe StateChange once (a second subscription would double-fire
            // OnConnectionStateChange, calling ReleasePermit twice — harmless since it no-ops
            // once the entry is gone, but pointless).
            // ConnectionOpening can fire again for a connection already tracked as holding a
            // permit — e.g. a redundant Open() call while the connection's ADO.NET state hasn't
            // yet transitioned to Closed/Broken, so ReleaseStalePermit above was a no-op. Without
            // this, AddOrUpdate would silently overwrite the tracked PermitBox, orphaning the
            // permit it replaces with nothing left to dispose it — a permanent, one-slot leak
            // from the shared admission budget per occurrence. Release what's being replaced
            // first, and only subscribe StateChange once (a second subscription would double-fire
            // OnConnectionStateChange, calling ReleasePermit twice — harmless since it no-ops
            // once the entry is gone, but pointless).
            if (_heldPermits.TryGetValue(connection, out var existing))
            {
                existing.Permit.Dispose();
            }
            else
            {
                connection.StateChange += OnConnectionStateChange;
            }

            _heldPermits.AddOrUpdate(connection, new PermitBox(permit));
        }
    }

    private void ReleasePermit(DbConnection connection)
    {
        lock (_permitLock)
        {
            if (_heldPermits.TryGetValue(connection, out var box))
            {
                _heldPermits.Remove(connection);
                connection.StateChange -= OnConnectionStateChange;
                box.Permit.Dispose();
            }
        }
    }

    private void ReleaseStalePermit(DbConnection connection)
    {
        if ((connection.State is ConnectionState.Closed) || (connection.State is ConnectionState.Broken))
        {
            ReleasePermit(connection);
        }
    }

    private void OnConnectionStateChange(object? sender, StateChangeEventArgs eventArgs)
    {
        if ((sender is DbConnection connection)
            && ((eventArgs.CurrentState is ConnectionState.Closed) || (eventArgs.CurrentState is ConnectionState.Broken)))
        {
            ReleasePermit(connection);
        }
    }
}
