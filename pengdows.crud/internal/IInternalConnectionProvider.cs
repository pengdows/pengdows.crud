// =============================================================================
// FILE: IInternalConnectionProvider.cs
// PURPOSE: Internal interface for connection acquisition from DatabaseContext.
//
// AI SUMMARY:
// - Internal interface hiding connection management implementation details.
// - Single method: GetConnection(ExecutionType, isShared).
// - ExecutionType: Read or Write determines connection selection.
// - isShared: Hints connection may be shared (affects pooling behavior).
// - Implemented by DatabaseContext, used by SqlContainer and TableGateway.
// - Keeps IDatabaseContext public API clean of connection management.
// - Returns ITrackedConnection with locking and lifecycle tracking.
// =============================================================================

using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

namespace pengdows.crud.@internal;

internal interface IInternalConnectionProvider
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);

    /// <summary>
    /// Async counterpart of <see cref="GetConnection"/> — genuinely non-blocking under pool
    /// contention. Async callers (command execution, async transaction creation) must use this
    /// instead of the sync <see cref="GetConnection"/>, which blocks the calling CLR ThreadPool
    /// thread inside PoolGovernor's blocking semaphore wait while a slot is unavailable.
    /// </summary>
    /// <remarks>
    /// The default implementation is a fake-async wrapper around the synchronous
    /// <see cref="GetConnection"/> — it still blocks the calling thread and ignores
    /// <paramref name="cancellationToken"/> (same convention as
    /// <c>IConnectionStrategy.GetConnectionAsync</c>/<c>HandleDialectDetectionAsync</c>). This
    /// keeps existing hand-rolled <see cref="IInternalConnectionProvider"/> test doubles
    /// source-compatible; <c>DatabaseContext</c> overrides this with the genuinely non-blocking
    /// implementation that matters for real pool contention.
    /// </remarks>
    ValueTask<ITrackedConnection> GetConnectionAsync(ExecutionType executionType, bool isShared = false,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<ITrackedConnection>(GetConnection(executionType, isShared));
    }

    ILockerAsync GetLock();

    void CloseAndDisposeConnection(ITrackedConnection? connection);

    ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection);
}
