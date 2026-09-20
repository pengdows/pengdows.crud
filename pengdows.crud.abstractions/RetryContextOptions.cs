using System;

namespace pengdows.crud;

/// <summary>
/// Per-context retry policy for a <see cref="RetryContext"/>, supplied once via the
/// <c>RetryContext(IDatabaseContext, RetryContextType, RetryContextOptions?)</c> constructor and
/// never mutated afterward — consistent with everything else about this design being fixed
/// before <c>StartAsync()</c> runs.
/// </summary>
/// <remarks>
/// The default values below are placeholders so the type is usable while sketching the shape of
/// the surrounding API. They are explicitly NOT a decided policy — see shortcoming #4 in
/// <c>docs/planning/retry-context-design.md</c>, which states backoff defaults are still
/// unspecified. Do not treat these numbers as final.
/// </remarks>
public sealed class RetryContextOptions
{
    /// <summary>
    /// Maximum number of attempts (including the first) before giving up. Placeholder default —
    /// see remarks.
    /// </summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>
    /// Base delay for decorrelated exponential jitter backoff. Placeholder default — see remarks.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Upper bound on any single backoff delay. Placeholder default — see remarks.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Optional ceiling on total elapsed time across all attempts. <c>null</c> means no ceiling
    /// beyond <see cref="MaxAttempts"/>.
    /// </summary>
    public TimeSpan? MaxElapsedTime { get; init; }

    /// <summary>
    /// Optional database-specific override for transient classification. When set, this is
    /// consulted instead of the default <c>DatabaseException.IsTransient</c> binding described in
    /// "Transient exception classification" in the design doc.
    /// </summary>
    /// <remarks>
    /// Typed over the plain BCL <see cref="Exception"/> rather than <c>DatabaseException</c> —
    /// the same reason <c>ISqlDialect.AnalyzeException(Exception)</c> is typed over
    /// <see cref="Exception"/>/<see cref="System.Data.Common.DbException"/> rather than this
    /// library's own concrete exception hierarchy: that hierarchy lives in the core
    /// <c>pengdows.crud</c> package, which this abstractions package cannot reference (the
    /// dependency runs the other way). <see cref="RetryContext"/> compensates at the call site:
    /// it only ever invokes this delegate when the exception being classified is already a
    /// <c>DatabaseException</c>, never for a row-count-policy violation, an execution-governance
    /// rejection, or <see cref="OperationCanceledException"/> — all of which are never retried
    /// regardless of what this override returns.
    /// </remarks>
    public Func<Exception, bool>? IsTransientOverride { get; init; }
}
