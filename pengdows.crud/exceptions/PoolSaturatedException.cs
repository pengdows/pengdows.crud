using System;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;

namespace pengdows.crud.exceptions;

public sealed class PoolSaturatedException : TimeoutException
{
    public PoolSaturatedException(PoolLabel label, string poolKeyHash, PoolStatisticsSnapshot snapshot, TimeSpan timeout)
        : base($"{label} pool saturated: {snapshot.InUse}/{snapshot.MaxPermits} in use, {snapshot.Queued} queued, timed out after {timeout} (key {poolKeyHash}).")
    {
        PoolLabel = label;
        PoolKeyHash = poolKeyHash;
        Snapshot = snapshot;
        Timeout = timeout;
    }

    public PoolLabel PoolLabel { get; }
    public string PoolKeyHash { get; }
    public PoolStatisticsSnapshot Snapshot { get; }
    public TimeSpan Timeout { get; }
}
