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
    /// Async counterpart to <see cref="GetConnection"/>. Async callers must use this instead of
    /// the sync overload -- the sync path blocks the calling thread inside PoolGovernor.Acquire()
    /// when a slot isn't immediately available, which starves the CLR ThreadPool under load
    /// (confirmed empirically: see AsyncPoolAcquisitionRegressionTests).
    /// </summary>
    ValueTask<ITrackedConnection> GetConnectionAsync(ExecutionType executionType, bool isShared = false,
        CancellationToken cancellationToken = default);

    ILockerAsync GetLock();

    void CloseAndDisposeConnection(ITrackedConnection? connection);

    ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection);
}
