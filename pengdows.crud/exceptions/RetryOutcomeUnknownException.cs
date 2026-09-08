// =============================================================================
// FILE: RetryOutcomeUnknownException.cs
// PURPOSE: Thrown when RetryContextType.Sequential deliberately stops retrying a transiently
//          failing command instead of guessing whether it already applied server-side.
//
// AI SUMMARY:
// - Not a DatabaseException: this is a "we refuse to guess" decision, not a database-error
//   classification. See "Commit ambiguity" (shortcoming #1) in
//   docs/planning/retry-context-design.md.
// - Never retried: RetryContext's retry loop only classifies DatabaseException instances; this
//   type deliberately falls outside that hierarchy so it always propagates immediately.
// - Thrown only for a command RetryContext cannot prove is safe to retry blind (not a DELETE, not
//   a [Version]-guarded UPDATE) and for which the caller did not declare
//   RetrySafety.IdempotentViaUniqueConstraint via IRetryContext.SetRetrySafety.
// =============================================================================

namespace pengdows.crud.exceptions;

public sealed class RetryOutcomeUnknownException : InvalidOperationException
{
    public RetryOutcomeUnknownException(Exception transientFailure, int attempt)
        : base(
            "RetryContext stopped retrying a transiently-failing command because its statement " +
            "shape is not provably safe to retry blind (not a DELETE, not a [Version]-guarded " +
            "UPDATE), and no RetrySafety.IdempotentViaUniqueConstraint was declared for it via " +
            "IRetryContext.SetRetrySafety. The command may or may not have actually applied " +
            "server-side before this failure — verify manually before deciding whether to " +
            "resubmit it. See \"Commit ambiguity\" in docs/planning/retry-context-design.md.",
            transientFailure)
    {
        Attempt = attempt;
    }

    /// <summary>The 1-based attempt number that produced the transient failure this wraps.</summary>
    public int Attempt { get; }
}
