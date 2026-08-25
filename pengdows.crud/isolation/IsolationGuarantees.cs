namespace pengdows.crud.isolation;

/// <summary>
/// The concurrency-control guarantees a concrete <see cref="System.Data.IsolationLevel"/> provides
/// on a specific database. This is dialect-owned data (<see cref="pengdows.crud.dialects.SqlDialect.GetIsolationGuarantees"/>)
/// because the same ADO.NET enum value means different things on different engines — e.g.
/// PostgreSQL's RepeatableRead is MVCC-based and non-blocking, while SQL Server's is lock-based
/// and blocking. <see cref="pengdows.crud.isolation.IsolationResolver"/> uses these as a partial
/// order (superset/subset of flags) to decide whether one level is strictly stronger, strictly
/// weaker, or genuinely incomparable to another — never by comparing raw enum values.
/// </summary>
[Flags]
internal enum IsolationGuarantees
{
    None = 0,

    /// <summary>No dirty reads: a transaction never sees another transaction's uncommitted writes.</summary>
    NoDirtyReads = 1,

    /// <summary>No non-repeatable reads: re-reading the same row within a transaction returns the same value.</summary>
    NoNonRepeatableReads = 2,

    /// <summary>No phantom reads: re-running the same range query within a transaction returns the same rows.</summary>
    NoPhantomReads = 4,

    /// <summary>MVCC-style: readers never block writers and writers never block readers.</summary>
    NonBlockingReads = 8,

    /// <summary>True serializability: no write-skew or other serialization anomalies survive.</summary>
    NoWriteSkew = 16
}
