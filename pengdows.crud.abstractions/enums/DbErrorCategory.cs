namespace pengdows.crud.enums;

/// <summary>
/// Classifies a database exception into a well-known error category for observability.
/// </summary>
/// <remarks>
/// Use this with <see cref="pengdows.crud.dialects.ISqlDialect.ClassifyException"/> to route
/// exceptions into specific metric counters so DBAs can distinguish deadlocks,
/// constraint violations, and serialization failures from generic failures.
/// </remarks>
public enum DbErrorCategory
{
    /// <summary>No classification applied (e.g., cancellation or pre-classified errors).</summary>
    None = 0,

    /// <summary>
    /// A deadlock was detected. The database rolled back the transaction to break the cycle.
    /// </summary>
    Deadlock = 1,

    /// <summary>
    /// A serialization failure occurred (e.g., snapshot isolation conflict, repeatable-read violation).
    /// The transaction should be retried.
    /// </summary>
    SerializationFailure = 2,

    /// <summary>
    /// A constraint violation occurred (unique, foreign key, not-null, or check constraint).
    /// </summary>
    ConstraintViolation = 3,

    /// <summary>
    /// A timeout occurred at the server or command level.
    /// Note: command timeouts are also tracked via <c>CommandsTimedOut</c> in metrics.
    /// </summary>
    Timeout = 4,

    /// <summary>
    /// A write operation was attempted on a connection opened in read-only mode.
    /// </summary>
    ReadOnlyViolation = 5,

    /// <summary>
    /// The database could not determine whether a statement/transaction actually committed
    /// (e.g. CockroachDB's SQLSTATE 40003, raised when its distributed consensus layer loses
    /// track of a commit's outcome during a network partition or node failure under
    /// contention/overload). Unlike <see cref="SerializationFailure"/>, this does NOT mean the
    /// operation is safe to blindly retry — it might have already taken effect. A caller should
    /// check actual outcome/idempotency state before retrying or accepting the write as applied.
    /// Deliberately excluded from the transient/retryable set in
    /// <see cref="pengdows.crud.dialects.ISqlDialect.AnalyzeException"/>'s default implementation.
    /// </summary>
    AmbiguousResult = 6,

    /// <summary>
    /// The exception could not be classified into a known category.
    /// </summary>
    Unknown = 99
}
