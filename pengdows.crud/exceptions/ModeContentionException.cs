// =============================================================================
// FILE: ModeContentionException.cs
// PURPOSE: Exception for DbMode contention timeout (SingleWriter/SingleConnection).
//
// AI SUMMARY:
// - Thrown when connection acquisition times out due to mode contention.
// - Extends TimeoutException with additional diagnostic properties.
// - Properties:
//   * Mode: DbMode that caused contention (SingleWriter, SingleConnection)
//   * Snapshot: ModeContentionSnapshot with waiter count and queue state
//   * Timeout: TimeSpan that was exceeded
// - Occurs in SingleWriter mode when write connection is held too long.
// - Indicates need for shorter transactions or different DbMode.
// - Message includes waiter count and timeout duration for diagnostics.
// =============================================================================

using pengdows.crud.enums;
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