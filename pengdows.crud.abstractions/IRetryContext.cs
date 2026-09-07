using pengdows.crud.enums;

namespace pengdows.crud;

/// <summary>
/// A governor-aware retry coordinator over a FIFO plan of <see cref="ISqlContainer"/> commands.
/// Extends <see cref="IDatabaseContext"/> so every existing <c>CreateSqlContainer</c>/<c>BuildX</c>
/// call site (including <c>TableGateway</c>/<c>PrimaryKeyTableGateway</c> Tier-1 builders) works
/// against it unchanged — those calls append to this context's internal queue instead of
/// executing immediately. Nothing hits the database until <see cref="StartAsync"/> runs.
/// </summary>
/// <remarks>
/// <para>
/// Full design: <c>docs/planning/retry-context-design.md</c> (tracked as FEAT-001). Status as of
/// this interface's introduction: designed, not implemented — see that document for the decided
/// and still-open pieces before relying on any particular runtime behavior here.
/// </para>
/// <para>
/// <b>Ownership boundary:</b> a <see cref="RetryContext"/> creates and owns each attempt's
/// transaction internally. It must not be obtained from inside an already-open
/// <c>ITransactionContext</c> — obtain it from a plain <see cref="IDatabaseContext"/>, the same
/// level <c>BeginTransactionAsync</c> sits at.
/// </para>
/// </remarks>
public interface IRetryContext : IDatabaseContext
{
    /// <summary>
    /// The execution mode this context was created with.
    /// </summary>
    RetryContextType RetryContextType { get; }

    /// <summary>
    /// The retry policy this context was created with. Fixed at construction; never mutated.
    /// </summary>
    RetryContextOptions Options { get; }

    /// <summary>
    /// True once <see cref="StartAsync"/> has been called. The command plan is immutable from
    /// that point on — further <c>CreateSqlContainer</c>/<c>BuildX</c> calls against this context
    /// must throw.
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// True once execution has reached a terminal state — completion, cancellation, or a terminal
    /// failure. <see cref="StartAsync"/> is defined to represent exactly one of those three
    /// outcomes; this reflects that it has done so.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Number of commands currently queued. Present for visibility/testing, not as a mechanism for
    /// callers to inspect or react to individual queue contents — this design has no notion of
    /// intermediate reads (see "Deliberate scope boundary" in the design doc).
    /// </summary>
    int QueuedCommandCount { get; }

    /// <summary>
    /// Attaches an optional affected-row validation policy to a command already queued on this
    /// context (i.e. a container previously returned by <c>CreateSqlContainer</c> or a Tier-1
    /// <c>BuildX</c> call against this context). See "Per-command affected-row policy" in the
    /// design doc. A violation aborts execution as non-transient and is never retried.
    /// </summary>
    /// <param name="container">A container already present in this context's queue.</param>
    /// <param name="policy">The policy to apply when that command executes.</param>
    void SetRowCountPolicy(ISqlContainer container, RowCountPolicy policy);

    /// <summary>
    /// Runs the queued command plan per <see cref="RetryContextType"/>'s semantics. Only one
    /// executor may run a given context, and only once — a second call must throw.
    /// </summary>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any pending backoff scheduling and performs cleanup. Must never silently convert
    /// an in-flight failure into an apparent success.
    /// </summary>
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
