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
    /// The exception could not be classified into a known category.
    /// </summary>
    Unknown = 99
}
