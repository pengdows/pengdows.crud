using System;
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