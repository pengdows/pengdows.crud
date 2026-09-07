namespace pengdows.crud.enums;

/// <summary>
/// Optional, per-command affected-row validation for a command queued on a
/// <see cref="RetryContext"/>. See <c>docs/planning/retry-context-design.md</c> —
/// "Per-command affected-row policy" — for the full rationale. A violation aborts execution as
/// non-transient; it is a data-integrity decision, not a database-error classification, and it
/// never triggers a retry.
/// </summary>
public enum RowCountPolicy
{
    /// <summary>
    /// No validation — whatever the provider reports is accepted as-is. Default for raw SQL,
    /// DDL, and stored procedures.
    /// </summary>
    Ignore = 0,

    /// <summary>
    /// Zero affected rows is a violation.
    /// </summary>
    AtLeastOne,

    /// <summary>
    /// Any affected-row count other than exactly one is a violation.
    /// </summary>
    ExactlyOne

    // An explicit [min, max] range variant is discussed in the design doc but not yet decided
    // for v1 — intentionally not represented here until it is.
}
