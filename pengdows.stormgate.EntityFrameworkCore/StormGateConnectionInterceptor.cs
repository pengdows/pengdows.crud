using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

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
/// A single instance must be shared across every <c>DbContextOptionsBuilder</c> it gates —
/// see <see cref="StormGateDbContextOptionsBuilderExtensions.UseStormGate(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, StormGateConnectionInterceptor)"/>.
/// Constructing a fresh interceptor per <c>DbContext</c> gives each its own semaphore and
/// throttles nothing across instances.
/// </remarks>
public sealed class StormGateConnectionInterceptor : DbConnectionInterceptor
{
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _acquireTimeout;
    private readonly ILogger _logger;

    // EF Core fires ConnectionFailed(Async) for ANY exception during an open attempt —
    // including a TimeoutException this interceptor itself threw from ConnectionOpening(Async)
    // because the gate was saturated, in which case no permit was ever acquired. Tracking which
    // specific DbConnection instances actually hold a permit lets Release only fire for
    // attempts that got one, instead of unconditionally over-releasing on every failure.
    private readonly ConditionalWeakTable<DbConnection, object> _heldPermits = new();
    private static readonly object PermitMarker = new();
    private readonly object _permitLock = new();

    public StormGateConnectionInterceptor(
        int maxConcurrentOpens,
        TimeSpan acquireTimeout,
        ILogger? logger = null)
    {
        if (maxConcurrentOpens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentOpens));
        }

        if (acquireTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(acquireTimeout));
        }

        _semaphore = new SemaphoreSlim(maxConcurrentOpens, maxConcurrentOpens);
        _acquireTimeout = acquireTimeout;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
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

    private void AcquirePermit(DbConnection connection)
    {
        ReleaseStalePermit(connection);

        if (!_semaphore.Wait(_acquireTimeout))
        {
            LogSaturation();
            throw new TimeoutException("Database is saturated (storm gate).");
        }

        MarkPermitHeld(connection);
    }

    private async ValueTask AcquirePermitAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        ReleaseStalePermit(connection);

        if (!await _semaphore.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false))
        {
            LogSaturation();
            throw new TimeoutException("Database is saturated (storm gate).");
        }

        MarkPermitHeld(connection);
    }

    private void MarkPermitHeld(DbConnection connection)
    {
        lock (_permitLock)
        {
            connection.StateChange += OnConnectionStateChange;
            _heldPermits.AddOrUpdate(connection, PermitMarker);
        }
    }

    private void ReleasePermit(DbConnection connection)
    {
        lock (_permitLock)
        {
            if (_heldPermits.Remove(connection))
            {
                connection.StateChange -= OnConnectionStateChange;
                _semaphore.Release();
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

    private void LogSaturation()
    {
        _logger.LogWarning(
            "StormGate saturation: timed out waiting for a connection permit after {Timeout}ms.",
            _acquireTimeout.TotalMilliseconds);
    }
}
