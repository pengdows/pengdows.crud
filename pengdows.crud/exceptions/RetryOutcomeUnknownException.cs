// =============================================================================
// FILE: RetryOutcomeUnknownException.cs
// PURPOSE: Thrown when RetryContext deliberately stops instead of guessing whether an attempt's
//          effects actually landed server-side.
//
// AI SUMMARY:
// - Not a DatabaseException: this is a "we refuse to guess" decision, not a database-error
//   classification. See "Commit ambiguity" (shortcoming #1) in
//   docs/planning/retry-context-design.md.
// - Never retried: RetryContext's retry loop only classifies DatabaseException instances; this
//   type deliberately falls outside that hierarchy so it always propagates immediately.
// - Thrown for exactly the cases where a confirmed rollback isn't available to prove nothing
//   applied: rollback for the failed attempt itself threw (outcome of that attempt is unknown),
//   a transient failure occurred during CommitAsync specifically (TransactionException.Phase ==
//   Commit — the write may have already landed server-side despite the exception), or
//   cancellation arrived after commit had already begun. A transient failure anywhere BEFORE
//   commit, followed by a rollback that itself completes without throwing, is retried instead —
//   confirmed rollback is proof nothing durably applied, regardless of the statement's shape.
// =============================================================================

namespace pengdows.crud.exceptions;

public sealed class RetryOutcomeUnknownException : InvalidOperationException
{
    public RetryOutcomeUnknownException(Exception transientFailure, int attempt)
        : base(
            "RetryContext stopped retrying because it could not confirm the failed attempt's " +
            "rollback, or a transient failure occurred during commit / after commit had already " +
            "begun. The command may or may not have actually applied server-side — verify " +
            "manually before deciding whether to resubmit it. See \"Commit ambiguity\" in " +
            "docs/planning/retry-context-design.md.",
            transientFailure)
    {
        Attempt = attempt;
    }

    /// <summary>The 1-based attempt number that produced the transient failure this wraps.</summary>
    public int Attempt { get; }
}
