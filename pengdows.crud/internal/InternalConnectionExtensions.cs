// =============================================================================
// FILE: InternalConnectionExtensions.cs
// PURPOSE: Extension method bridging IDatabaseContext to internal connection provider.
//
// AI SUMMARY:
// - Extension method for IDatabaseContext to access connection internals.
// - GetConnection(): Casts context to IInternalConnectionProvider and calls it.
// - Throws InvalidOperationException if context doesn't implement provider.
// - Allows internal components to get connections via IDatabaseContext interface.
// - Keeps implementation details hidden from public API consumers.
// - Used by SqlContainer, TableGateway for connection acquisition.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace pengdows.crud;

internal static class InternalConnectionExtensions
{
    internal static ITrackedConnection GetConnection(this IDatabaseContext context, ExecutionType executionType,
        bool isShared = false)
    {
        if (context is not IInternalConnectionProvider provider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        return provider.GetConnection(executionType, isShared);
    }
}
