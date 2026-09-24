// =============================================================================
// FILE: IInternalConnectionProvider.cs
// PURPOSE: Internal interface for connection acquisition from DatabaseContext.
//
// AI SUMMARY:
// - Internal interface hiding connection management implementation details.
// - Methods: GetConnection/GetConnectionAsync(ExecutionType, isShared), GetLock(),
//   CloseAndDisposeConnection/CloseAndDisposeConnectionAsync.
// - ExecutionType: Read or Write determines connection selection.
// - isShared: marks the connection as shared (TrackedConnection then hands out a real lock).
// - Implemented by DatabaseContext and TransactionContext, used by SqlContainer
//   (directly and via InternalConnectionExtensions).
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
    /// Async counterpart of <see cref="GetConnection"/>. Genuinely awaits pool-slot acquisition
    /// (via PoolGovernor.AcquireAsync) instead of blocking the calling thread on the synchronous
    /// PoolGovernor.Acquire() -- see AsyncConnectionAcquisitionTests for the regression this
    /// exists to prevent (sync-over-async CLR ThreadPool starvation under pool contention).
    /// </summary>
    ValueTask<ITrackedConnection> GetConnectionAsync(ExecutionType executionType, bool isShared = false,
        CancellationToken cancellationToken = default);

    ILockerAsync GetLock();

    void CloseAndDisposeConnection(ITrackedConnection? connection);

    ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection);
}
