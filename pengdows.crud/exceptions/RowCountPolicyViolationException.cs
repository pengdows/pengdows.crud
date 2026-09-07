// =============================================================================
// FILE: RowCountPolicyViolationException.cs
// PURPOSE: Thrown when a RetryContext-queued command's affected-row count violates
//          its attached RowCountPolicy.
//
// AI SUMMARY:
// - Not a DatabaseException: this is a data-integrity decision about a command that
//   executed successfully, not a database-error classification. See "Per-command
//   affected-row policy" in docs/planning/retry-context-design.md.
// - Never retried: RetryContext's retry loop only classifies DatabaseException
//   instances; this type deliberately falls outside that hierarchy so it always
//   propagates immediately and aborts execution.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.exceptions;

public sealed class RowCountPolicyViolationException : InvalidOperationException
{
    public RowCountPolicyViolationException(RowCountPolicy policy, int actualRowsAffected)
        : base($"Command violated its {policy} row-count policy: {actualRowsAffected} row(s) affected.")
    {
        Policy = policy;
        ActualRowsAffected = actualRowsAffected;
    }

    public RowCountPolicy Policy { get; }
    public int ActualRowsAffected { get; }
}
