namespace pengdows.crud.enums;

/// <summary>
/// Selects <see cref="RetryContext"/>'s execution mode for its queued command plan.
/// See <c>docs/planning/retry-context-design.md</c> — "Dual retry modes, both queue-driven" —
/// for the full semantics of each value.
/// </summary>
public enum RetryContextType
{
    /// <summary>
    /// The whole queue runs inside one transaction, all-or-nothing. A transient failure at any
    /// point rolls back and retries the entire queue, from the first command, in a fresh
    /// transaction.
    /// </summary>
    Transactional,

    /// <summary>
    /// Commands execute one at a time against the plain parent context, no transaction involved.
    /// A command that succeeds is removed from the queue and never re-executed; a command that
    /// fails transiently is retried in place.
    /// </summary>
    Sequential
}
