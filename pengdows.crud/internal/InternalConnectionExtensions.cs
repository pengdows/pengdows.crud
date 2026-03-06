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

using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.threading;
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

    internal static ILockerAsync GetLock(this IDatabaseContext context)
    {
        if (context is not IInternalConnectionProvider provider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        return provider.GetLock();
    }

    internal static void CloseAndDisposeConnection(this IDatabaseContext context, ITrackedConnection? connection)
    {
        if (context is not IInternalConnectionProvider provider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        provider.CloseAndDisposeConnection(connection);
    }

    internal static ValueTask CloseAndDisposeConnectionAsync(this IDatabaseContext context,
        ITrackedConnection? connection)
    {
        if (context is not IInternalConnectionProvider provider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        return provider.CloseAndDisposeConnectionAsync(connection);
    }
}
