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

using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.@internal;

internal interface IInternalConnectionProvider
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);
}
