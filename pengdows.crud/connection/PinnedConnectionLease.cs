// =============================================================================
// FILE: PinnedConnectionLease.cs
// PURPOSE: One connection held for a short sequence of commands outside a transaction.
//
// AI SUMMARY:
// - GEN-001 / CORE-016: a session-scoped last-id function (Informix DBINFO, SAP HANA
//   CURRENT_IDENTITY_VALUE(), Access @@IDENTITY) reports the id only on the connection that ran the
//   INSERT, so the INSERT and the id query must share one connection. No transaction is opened:
//   that would cost round trips, change the caller's isolation, and an unlogged Informix database
//   rejects BEGIN WORK.
// - Acquired through the context like any operation's connection, so it carries the pool slot (and
//   under SingleWriter the write permit). A SqlContainer with PinnedConnection set uses it and never
//   releases it; the lease releases it exactly once, when disposed.
// - Inside a caller's transaction no lease is needed: the transaction already pins its connection.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace pengdows.crud.connection;

internal sealed class PinnedConnectionLease : IAsyncDisposable
{
    private readonly IInternalConnectionProvider _owner;
    private int _released;

    private PinnedConnectionLease(IInternalConnectionProvider owner, ITrackedConnection connection)
    {
        _owner = owner;
        Connection = connection;
    }

    internal ITrackedConnection Connection { get; }

    internal static async ValueTask<PinnedConnectionLease> AcquireAsync(IDatabaseContext context,
        ExecutionType executionType, CancellationToken cancellationToken)
    {
        if (context is not IInternalConnectionProvider provider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        var isShared = context.ConnectionMode == DbMode.SingleConnection;
        var connection = await provider.GetConnectionAsync(executionType, isShared, cancellationToken)
            .ConfigureAwait(false);
        return new PinnedConnectionLease(provider, connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            await _owner.CloseAndDisposeConnectionAsync(Connection).ConfigureAwait(false);
        }
    }
}
