// =============================================================================
// FILE: PoolStatisticsSnapshot.cs
// PURPOSE: Immutable snapshot of connection pool statistics.
//
// AI SUMMARY:
// - Readonly record struct for pool metrics capture.
// - Fields:
//   * Label: PoolLabel (Reader or Writer)
//   * PoolKeyHash: Hashed identifier for the pool
//   * MaxPermits: Configured maximum pool size
//   * InUse: Currently acquired permits
//   * PeakInUse: Highest concurrent usage observed
//   * Queued: Operations waiting for permits
//   * TotalAcquired: Lifetime permit acquisitions
//   * TotalTimeouts: Acquisition timeout count
//   * TotalCanceledWaits: Canceled acquisition count
//   * Disabled: Whether governor is disabled
// - Used by PoolSaturatedException for diagnostic context.
// - Thread-safe: All values captured atomically from PoolGovernor.
// =============================================================================

using pengdows.crud.infrastructure;

namespace pengdows.crud.metrics;

public readonly record struct PoolStatisticsSnapshot(
    PoolLabel Label,
    string PoolKeyHash,
    int MaxPermits,
    int InUse,
    int PeakInUse,
    int Queued,
    long TotalAcquired,
    long TotalTimeouts,
    long TotalCanceledWaits,
    bool Disabled);