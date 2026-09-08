namespace pengdows.crud.enums;

/// <summary>
/// Opt-in override for a queued command's retry-safety classification on a
/// <see cref="RetryContext"/> running <see cref="RetryContextType.Sequential"/>. See
/// <c>docs/planning/retry-context-design.md</c> — shortcoming #1, "Commit ambiguity" — for the
/// full rationale.
/// </summary>
/// <remarks>
/// A transiently-failing command is automatically safe to retry in place, with no declaration
/// needed, when it is a <c>DELETE</c> (deleting an already-deleted row affects 0 rows, not an
/// error, not a duplicate effect) or an <c>UPDATE</c> guarded by a <c>[Version]</c>
/// (optimistic-concurrency) column (the version WHERE clause is self-limiting: if the earlier
/// attempt actually landed, the version already advanced, so the retry affects 0 rows instead of
/// double-applying). Everything else — a bare <c>INSERT</c>, or an <c>UPDATE</c> with no version
/// guard — is genuinely unsafe to retry blind: RetryContext fails closed instead, throwing
/// <see cref="pengdows.crud.exceptions.RetryOutcomeUnknownException"/> rather than guessing,
/// unless the caller declares <see cref="IdempotentViaUniqueConstraint"/>.
/// </remarks>
public enum RetrySafety
{
    /// <summary>
    /// No declaration — RetryContext auto-classifies the command from its own SQL/parameter
    /// shape (see remarks). This is the default for every queued command.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The caller asserts this command is safe to retry as-is because it is protected by its own
    /// unique constraint (e.g. a client-generated idempotency key column with a unique index): if
    /// an earlier attempt's execution already landed server-side before the failure, a retry
    /// hitting that same constraint reports a
    /// <see cref="pengdows.crud.exceptions.UniqueConstraintViolationException"/>, which
    /// RetryContext then treats as confirmation of success (the command is dequeued, not treated
    /// as a real conflict) rather than propagating it — the same pattern Stripe's API uses for
    /// exactly this problem.
    /// </summary>
    IdempotentViaUniqueConstraint = 1
}
