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
