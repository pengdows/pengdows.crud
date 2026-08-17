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
        if (!_semaphore.Wait(_acquireTimeout))
        {
            _logger.LogWarning(
                "StormGate saturation: timed out waiting for a connection permit after {Timeout}ms.",
                _acquireTimeout.TotalMilliseconds);
            throw new TimeoutException("Database is saturated (storm gate).");
        }

        _heldPermits.AddOrUpdate(connection, PermitMarker);
        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (!await _semaphore.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "StormGate saturation: timed out waiting for a connection permit after {Timeout}ms.",
                _acquireTimeout.TotalMilliseconds);
            throw new TimeoutException("Database is saturated (storm gate).");
        }

        _heldPermits.AddOrUpdate(connection, PermitMarker);
        return result;
    }

    // A permit acquired in ConnectionOpening(Async) but whose physical open then throws
    // must still be released — otherwise a single failed connection attempt permanently
    // shrinks the gate's capacity. Release only if THIS connection actually holds a permit —
    // see the _heldPermits comment above for why that check is required.
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        if (_heldPermits.Remove(connection))
        {
            _semaphore.Release();
        }
    }

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_heldPermits.Remove(connection))
        {
            _semaphore.Release();
        }

        return Task.CompletedTask;
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (_heldPermits.Remove(connection))
        {
            _semaphore.Release();
        }
    }

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (_heldPermits.Remove(connection))
        {
            _semaphore.Release();
        }

        return Task.CompletedTask;
    }
}
