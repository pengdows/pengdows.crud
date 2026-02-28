// =============================================================================
// FILE: PoolStatisticsSnapshot.cs
// PURPOSE: Immutable snapshot of connection pool statistics.
//
// AI SUMMARY:
// - Readonly record struct for pool metrics capture.
// - Fields:
//   * Label: PoolLabel (Reader or Writer)
//   * PoolKeyHash: Hashed identifier for the pool
//   * MaxSlots: Configured maximum pool size
//   * InUse: Currently acquired slots
//   * PeakInUse: Highest concurrent usage observed
//   * Queued: Operations waiting for slots
//   * TotalAcquired: Lifetime slot acquisitions
//   * TotalSlotTimeouts: Slot (semaphore) acquisition timeout count
//   * TotalTurnstileTimeouts: Turnstile acquisition timeout count (separate from slot timeouts)
//   * TotalCanceledWaits: Canceled acquisition count
//   * Disabled: Governor is disabled — returns default slot without contention management
//   * Forbidden: Governor is forbidden (MaxPoolSize=0) — throws on any Acquire attempt
// - Used by PoolSaturatedException for diagnostic context.
// - Thread-safe: All values captured atomically from PoolGovernor.
// - Distinguishes between two timeout sources:
//     TotalTimeouts          = timed out waiting for a connection slot (semaphore)
//     TotalTurnstileTimeouts = timed out waiting for the fairness turnstile
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.metrics;

public readonly record struct PoolStatisticsSnapshot(
    PoolLabel Label,
    string PoolKeyHash,
    int MaxSlots,
    int InUse,
    int PeakInUse,
    int Queued,
    int PeakQueued,
    int TurnstileQueued,
    int PeakTurnstileQueued,
    long TotalAcquired,
    long TotalWaitTicks,
    long TotalHoldTicks,
    long TotalSlotTimeouts,
    long TotalTurnstileTimeouts,
    long TotalCanceledWaits,
    bool Disabled,
    bool Forbidden);
