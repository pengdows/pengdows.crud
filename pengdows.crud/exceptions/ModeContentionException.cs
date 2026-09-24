// =============================================================================
// FILE: ModeContentionException.cs
// PURPOSE: Exception for DbMode contention timeout (SingleWriter/SingleConnection).
//
// AI SUMMARY:
// - Thrown when connection acquisition times out due to mode contention.
// - Extends TimeoutException with additional diagnostic properties.
// - Properties:
//   * Mode: DbMode that caused contention (SingleWriter, SingleConnection)
//   * Snapshot: ModeContentionSnapshot with current/peak waiters, wait and timeout totals
//   * Timeout: TimeSpan that was exceeded (ModeLockTimeout)
// - Occurs when a shared connection's lock (SingleWriter/SingleConnection) or the
//   SingleConnection transaction gate is held longer than ModeLockTimeout.
// - Indicates need for shorter transactions or different DbMode.
// - Message includes waiter count and timeout duration for diagnostics.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;

namespace pengdows.crud.exceptions;

public sealed class ModeContentionException : TimeoutException
{
    public ModeContentionException(DbMode mode, ModeContentionSnapshot snapshot, TimeSpan timeout)
        : base($"{mode} contention: {snapshot.CurrentWaiters} waiters, timed out after {timeout}.")
    {
        Mode = mode;
        Snapshot = snapshot;
        Timeout = timeout;
    }

    public DbMode Mode { get; }
    public ModeContentionSnapshot Snapshot { get; }
    public TimeSpan Timeout { get; }
}