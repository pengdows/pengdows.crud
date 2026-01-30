// =============================================================================
// FILE: PoolSaturatedException.cs
// PURPOSE: Exception when connection pool is exhausted and timeout expires.
//
// AI SUMMARY:
// - Thrown when all pool connections are in use and wait times out.
// - Extends TimeoutException with rich diagnostic properties.
// - Properties:
//   * PoolLabel: Identifies which pool (e.g., Write, Read)
//   * PoolKeyHash: Hashed pool key for correlation
//   * Snapshot: PoolStatisticsSnapshot with InUse, MaxPermits, Queued counts
//   * Timeout: TimeSpan that was exceeded waiting for connection
// - Message format: "{Label} pool saturated: {InUse}/{Max} in use, {Queued} queued"
// - Indicates need for: larger pool, shorter connection hold times, connection leaks.
// - Use metrics snapshot to diagnose pool pressure patterns.
// =============================================================================

using pengdows.crud.infrastructure;
using pengdows.crud.metrics;

namespace pengdows.crud.exceptions;

public sealed class PoolSaturatedException : TimeoutException
{
    public PoolSaturatedException(PoolLabel label, string poolKeyHash, PoolStatisticsSnapshot snapshot,
        TimeSpan timeout)
        : base(
            $"{label} pool saturated: {snapshot.InUse}/{snapshot.MaxPermits} in use, {snapshot.Queued} queued, timed out after {timeout} (key {poolKeyHash}).")
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