// =============================================================================
// FILE: RetryContext.cs
// PURPOSE: RetryContext subsystem (FEAT-001) — see docs/planning/retry-context-design.md for the
//          full design.
//
// STATUS: Both RetryContextType.Sequential (FIFO, one command per individual transaction,
// dequeuing each command as it succeeds) and RetryContextType.Transactional (the whole set of
// queued commands re-run as one transaction per attempt, all-or-nothing) are implemented, both
// using DatabaseException.IsTransient-based retry with decorrelated exponential jitter backoff and
// per-command RowCountPolicy enforcement, per "Dual retry modes" and "Per-command affected-row
// policy" in the design doc. Commit-ambiguity (shortcoming #1) IS implemented for both modes, in
// two different shapes matching how each mode is actually vulnerable to it: for Sequential, a
// transiently-failing command not provably safe to retry blind (not a DELETE, not a
// [Version]-guarded UPDATE) fails closed with RetryOutcomeUnknownException unless the caller
// declared RetrySafety.IdempotentViaUniqueConstraint via SetRetrySafety — see
// RunSequentialAttemptAsync; for Transactional, a mid-batch execution failure is always safe to
// retry as a whole (rollback already undid everything), so the only real risk is a CommitAsync
// itself throwing transiently — detected via TransactionException.Phase == TransactionPhase.Commit
// and, likewise, failed closed with RetryOutcomeUnknownException rather than blindly re-running
// the entire batch — see RunTransactionalAttemptAsync.
//
// EXECUTION MODEL: Timer-driven, not one continuous awaited loop, for either mode. StartAsync arms
// a one-shot System.Threading.Timer for the first attempt and returns only once a
// TaskCompletionSource the timer callback chain resolves. Every firing disarms the timer FIRST
// (Change(Infinite, Infinite)) before doing anything else, and only re-arms it once that attempt's
// outcome is fully decided — the classic "stop the timer on entry, conditionally re-enable it on
// exit" discipline, so attempt N+1 (or its backoff wait) can never start while attempt N is still
// actually in flight. A RetryContext instance runs exactly one RetryContextType for its whole
// lifetime, so exactly one timer/backoff/attempt-counter set is armed — see RunStepAsync for where
// the two modes' actual per-attempt work diverges.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;

namespace pengdows.crud;

/// <inheritdoc cref="IRetryContext"/>
public sealed class RetryContext : SafeAsyncDisposableBase, IRetryContext
{
    private readonly IDatabaseContext _inner;
    private readonly Queue<ISqlContainer> _queue = new();
    private readonly List<ISqlContainer> _allContainers = new();
    private readonly Dictionary<ISqlContainer, RowCountPolicy> _rowCountPolicies = new();
    private readonly Dictionary<ISqlContainer, RetrySafety> _retrySafety = new();
    private readonly CancellationTokenSource _stopCts = new();
    private int _started;
    private int _completed;

    // Timer-driven run state — only ever touched by one attempt's worth of callback chain at a
    // time (see the class-level "EXECUTION MODEL" note above), so plain fields are safe: nothing
    // else can be concurrently mutating them while a run is in flight. A RetryContext instance
    // only ever runs one RetryContextType for its whole lifetime (decided at construction), so
    // there is exactly one timer/backoff/attempt-counter set here, not one per mode.
    private Timer? _timer;
    private TaskCompletionSource? _completion;
    private Stopwatch? _stopwatch;
    private TimeSpan _previousDelay;
    private int _attempt;

    public RetryContext(IDatabaseContext context, RetryContextType retryContextType, RetryContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Ownership boundary (docs/planning/retry-context-design.md, "Fourth revision note"):
        // RetryContext creates and owns each attempt's transaction internally. It must not be
        // nested inside an already-open transaction.
        if (context is ITransactionContext)
        {
            throw new NotSupportedException(
                "RetryContext cannot be constructed from within an active transaction. It creates " +
                "and owns each attempt's transaction internally — obtain it from a plain " +
                "IDatabaseContext, the same level BeginTransactionAsync sits at, not from inside an " +
                "already-open transaction. See the ownership boundary note in " +
                "docs/planning/retry-context-design.md.");
        }

        _inner = context;
        RetryContextType = retryContextType;
        Options = options ?? new RetryContextOptions();
    }

    public RetryContextType RetryContextType { get; }

    public RetryContextOptions Options { get; }

    /// <summary>
    /// The plain <see cref="IDatabaseContext"/> this RetryContext wraps and forwards everything
    /// to. Not part of the public contract - internal escape hatch for infrastructure (currently
    /// <see cref="TableGateway{TEntity,TRowID}"/>'s per-dialect template-priming) that needs a
    /// context whose <c>CreateSqlContainer()</c> has no side effect, for a container that is never
    /// actually executed itself. See docs/planning/retry-context-design.md.
    /// </summary>
    internal IDatabaseContext WrappedContext => _inner;

    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public int QueuedCommandCount => _queue.Count;

    public void SetRowCountPolicy(ISqlContainer container, RowCountPolicy policy)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(container);
        if (!_queue.Contains(container))
        {
            throw new ArgumentException(
                "The container is not part of this RetryContext's queue.", nameof(container));
        }

        _rowCountPolicies[container] = policy;
    }

    /// <inheritdoc cref="IRetryContext.SetRetrySafety"/>
    public void SetRetrySafety(ISqlContainer container, RetrySafety retrySafety)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(container);
        if (!_queue.Contains(container))
        {
            throw new ArgumentException(
                "The container is not part of this RetryContext's queue.", nameof(container));
        }

        _retrySafety[container] = retrySafety;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "StartAsync has already been called on this RetryContext; only one executor may " +
                "run a given context.");
        }

        if (RetryContextType != RetryContextType.Sequential && RetryContextType != RetryContextType.Transactional)
        {
            Volatile.Write(ref _completed, 1);
            throw new NotSupportedException($"Unknown RetryContextType: {RetryContextType}");
        }

        try
        {
            await RunTimerDrivenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _completed, 1);
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _stopCts.Cancel();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Arms the single timer-driven attempt loop and returns once the whole run resolves — see
    /// the class-level "EXECUTION MODEL" note. A RetryContext instance runs exactly one
    /// <see cref="RetryContextType"/> for its entire lifetime (<see cref="StartAsync"/> already
    /// rejected any unimplemented mode before this is ever reached), so there is exactly one
    /// timer and one backoff/attempt-counter set here, not one per mode. This method only arms
    /// the first attempt; it does not itself execute anything — <see cref="RunStepAsync"/>'s
    /// callback chain does the actual work and resolves <c>_completion</c> when the run concludes.
    /// </summary>
    private ValueTask RunTimerDrivenAsync(CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        _stopwatch = Stopwatch.StartNew();
        _attempt = 0;
        _previousDelay = TimeSpan.Zero;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;

        // Responds to cancellation immediately, independent of whatever the timer is currently
        // doing — without this, a cancel requested while the timer is idle mid-backoff wouldn't be
        // noticed until that timer eventually fired.
        var cancelRegistration = linked.Token.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            completion);

        _timer = new Timer(OnTimerFired, linked.Token, Timeout.Infinite, Timeout.Infinite);

        // The only place StartAsync itself schedules anything — every attempt after this one is
        // scheduled by the timer callback chain below, not by this method.
        ScheduleNextAttempt(TimeSpan.Zero);

        return AwaitAndCleanUpAsync(completion, linked, cancelRegistration);
    }

    private async ValueTask AwaitAndCleanUpAsync(
        TaskCompletionSource completion,
        CancellationTokenSource linked,
        CancellationTokenRegistration cancelRegistration)
    {
        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            cancelRegistration.Dispose();
            _timer?.Dispose();
            _timer = null;
            linked.Dispose();
        }
    }

    /// <summary>
    /// The timer callback. Stops the timer first thing, before anything else runs, so nothing can
    /// re-fire while this attempt (or the scheduling decision at the end of it) is still in
    /// progress — see the class-level "EXECUTION MODEL" note. Explicit and technically redundant
    /// with the timer's one-shot (<see cref="Timeout.Infinite"/> period) configuration, kept for
    /// the same discipline/documentation value regardless.
    /// </summary>
    private void OnTimerFired(object? state)
    {
        try
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _ = RunStepAsync((CancellationToken)state!);
        }
        catch (ObjectDisposedException)
        {
            // A callback that was already queued raced RetryContext disposing the timer after the
            // run concluded through some other path. The run is over either way — nothing to do.
        }
    }

    /// <summary>
    /// Executes exactly one attempt, dispatched by <see cref="RetryContextType"/>, then either
    /// resolves <c>_completion</c> (nothing left to do / non-transient or budget-exhausted failure
    /// / cancellation) or re-arms the timer for the next step (success moving on, or a transient
    /// failure's backoff wait) and returns. Never throws past its own boundary — every exception is
    /// funneled into <c>_completion</c> instead, since this runs as a timer callback's
    /// fire-and-forget continuation, not something anyone awaits directly.
    /// </summary>
    private async Task RunStepAsync(CancellationToken cancellationToken)
    {
        var completion = _completion!;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (RetryContextType)
            {
                case RetryContextType.Sequential:
                    await RunSequentialAttemptAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case RetryContextType.Transactional:
                    await RunTransactionalAttemptAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    /// <summary>
    /// One Sequential attempt: opens a fresh transaction, executes and commits the current queue
    /// head against it individually, then either dequeues on success or rolls back on failure. A
    /// command that succeeds is removed from the queue and never re-executed; a command that
    /// fails transiently is retried in place — subject to the statement-shape safety check below
    /// — up to
    /// <see cref="RetryContextOptions.MaxAttempts"/>/<see cref="RetryContextOptions.MaxElapsedTime"/>.
    /// See "Dual retry modes" in the design doc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each command gets its own individual transaction rather than relying on implicit
    /// per-statement auto-commit — this makes the failure/rollback path here structurally
    /// identical to <see cref="RunTransactionalAttemptAsync"/> (just scoped to one command instead
    /// of the whole queue), and gives the same explicit commit-failure surface that method already
    /// has instead of a bare, un-rollback-able auto-commit.
    /// </para>
    /// <para>
    /// Statement-shape commit-ambiguity handling (shortcoming #1) IS implemented here: a
    /// transiently-failing command that is not provably safe to retry blind — not a
    /// <c>DELETE</c>, not a <c>[Version]</c>-guarded <c>UPDATE</c>, and not declared
    /// <see cref="RetrySafety.IdempotentViaUniqueConstraint"/> via
    /// <see cref="SetRetrySafety"/> — fails closed with
    /// <see cref="RetryOutcomeUnknownException"/> instead of blindly retrying. A command that *is*
    /// declared idempotent-via-unique-constraint additionally treats a
    /// <see cref="UniqueConstraintViolationException"/> on a retry attempt (never on the first
    /// attempt) as confirmation of success rather than a real conflict. Still NOT implemented: a
    /// <c>CommitAsync</c> that itself throws transiently is currently classified the same as any
    /// other transient failure by this same statement-shape check — see
    /// <see cref="RunTransactionalAttemptAsync"/>'s identical disclaimer for why that narrower
    /// window remains a known, separate gap.
    /// </para>
    /// </remarks>
    private async Task RunSequentialAttemptAsync(CancellationToken cancellationToken)
    {
        var completion = _completion!;

        if (_queue.Count == 0)
        {
            completion.TrySetResult();
            return;
        }

        var template = _queue.Peek();

        // Bound this attempt to whatever's left of MaxElapsedTime — otherwise a single
        // slow/hanging command could run indefinitely, past the overall retry budget, with
        // nothing but the provider's own (often unset) command timeout to stop it.
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Options.MaxElapsedTime is { } budget)
        {
            var remaining = budget - _stopwatch!.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new OperationCanceledException(
                    "RetryContext's MaxElapsedTime budget was already exhausted before this " +
                    "attempt could start.");
            }

            attemptCts.CancelAfter(remaining);
        }

        _attempt++;

        var txn = await _inner.BeginTransactionAsync(cancellationToken: attemptCts.Token).ConfigureAwait(false);
        try
        {
            await using var clone = template.Clone(txn);
            var rowsAffected = await clone.ExecuteNonQueryAsync(CommandType.Text, attemptCts.Token)
                .ConfigureAwait(false);
            EnforceRowCountPolicy(template, rowsAffected);

            await txn.CommitAsync(attemptCts.Token).ConfigureAwait(false);

            _queue.Dequeue();
            _attempt = 0;
            _previousDelay = TimeSpan.Zero;
            ScheduleNextAttempt(TimeSpan.Zero);
        }
        catch (UniqueConstraintViolationException) when (
            _attempt > 1 && GetRetrySafety(template) == RetrySafety.IdempotentViaUniqueConstraint)
        {
            // Not the first attempt, and the caller declared this command safe to retry via its
            // own unique constraint — an earlier attempt's execution already landed server-side
            // before that attempt's failure, and this constraint violation is the proof of that,
            // not a real conflict.
            await SafeRollbackAsync(txn, attemptCts.Token).ConfigureAwait(false);

            _queue.Dequeue();
            _attempt = 0;
            _previousDelay = TimeSpan.Zero;
            ScheduleNextAttempt(TimeSpan.Zero);
        }
        catch (DatabaseException ex) when (IsTransient(ex))
        {
            await SafeRollbackAsync(txn, attemptCts.Token).ConfigureAwait(false);

            var budgetExceeded =
                Options.MaxElapsedTime is { } maxElapsed && _stopwatch!.Elapsed >= maxElapsed;
            if (_attempt >= Options.MaxAttempts || budgetExceeded)
            {
                throw;
            }

            if (ClassifyStatementShape(template) == StatementShape.Other &&
                GetRetrySafety(template) != RetrySafety.IdempotentViaUniqueConstraint)
            {
                throw new RetryOutcomeUnknownException(ex, _attempt);
            }

            _previousDelay = NextDelay(_previousDelay);
            ScheduleNextAttempt(_previousDelay);
        }
        catch
        {
            await SafeRollbackAsync(txn, attemptCts.Token).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await txn.DisposeAsync().ConfigureAwait(false);
        }
    }

    private enum StatementShape
    {
        /// <summary>Naturally safe to retry: deleting an already-deleted row affects 0 rows.</summary>
        Delete,

        /// <summary>
        /// Naturally safe to retry: the <c>[Version]</c> WHERE clause is self-limiting — if the
        /// earlier attempt already landed, the version already advanced, so the retry affects 0
        /// rows instead of double-applying.
        /// </summary>
        VersionGuardedUpdate,

        /// <summary>
        /// Not provably safe to retry blind (bare <c>INSERT</c>, or an <c>UPDATE</c>/anything else
        /// with no version guard) — requires <see cref="RetrySafety.IdempotentViaUniqueConstraint"/>
        /// to retry at all.
        /// </summary>
        Other
    }

    /// <summary>
    /// Classifies a queued command's SQL shape for the commit-ambiguity policy (see the
    /// class-level remarks on <see cref="RunSequentialAttemptAsync"/>). <c>DELETE</c> is detected
    /// from the statement's leading keyword; a version-guarded <c>UPDATE</c> is detected via the
    /// presence of a "v0" parameter — this library's own documented, stable naming convention
    /// (see <c>docs/parameter-naming-convention.md</c>) for a <see cref="TableGateway{TEntity,TRowID}"/>-built
    /// version-guard WHERE clause, not a guess at arbitrary caller SQL structure.
    /// </summary>
    private static StatementShape ClassifyStatementShape(ISqlContainer container)
    {
        var sql = container.Query.ToString().AsSpan().TrimStart();
        if (sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            return StatementShape.Delete;
        }

        if (sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && HasVersionGuardParameter(container))
        {
            return StatementShape.VersionGuardedUpdate;
        }

        return StatementShape.Other;
    }

    private static bool HasVersionGuardParameter(ISqlContainer container)
    {
        try
        {
            container.GetParameterValue("v0");
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private RetrySafety GetRetrySafety(ISqlContainer container) =>
        _retrySafety.TryGetValue(container, out var safety) ? safety : RetrySafety.Unspecified;

    /// <summary>
    /// One Transactional attempt: opens a fresh transaction against the plain parent context,
    /// clones and executes every queued command against it in order, and commits only if all of
    /// them succeed. Nothing is ever dequeued — a failed attempt's transaction is rolled back in
    /// full and, if the failure is transient, the *entire* set of commands is retried again from
    /// scratch in a brand-new transaction on the next attempt (subject to
    /// <see cref="RetryContextOptions.MaxAttempts"/>/<see cref="RetryContextOptions.MaxElapsedTime"/>).
    /// See "Dual retry modes" in the design doc.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="RunSequentialAttemptAsync"/>, statement-shape commit-ambiguity detection
    /// (shortcoming #1) does not apply here, and deliberately so — a mid-transaction failure
    /// (anywhere in the <c>foreach</c> loop, before <c>CommitAsync</c> is ever reached) is always
    /// safe to retry as a whole, since rollback already undid every command in this attempt. The
    /// narrower remaining ambiguity — a <c>CommitAsync</c> that itself throws transiently, where
    /// the transaction may have already committed server-side despite the exception — IS handled:
    /// a <see cref="TransactionException"/> whose <see cref="TransactionException.Phase"/> is
    /// <see cref="TransactionPhase.Commit"/> fails closed with
    /// <see cref="RetryOutcomeUnknownException"/> unconditionally, rather than blindly retrying
    /// the entire batch in a brand-new transaction.
    /// </remarks>
    private async Task RunTransactionalAttemptAsync(CancellationToken cancellationToken)
    {
        var completion = _completion!;

        if (_allContainers.Count == 0)
        {
            completion.TrySetResult();
            return;
        }

        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Options.MaxElapsedTime is { } budget)
        {
            var remaining = budget - _stopwatch!.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new OperationCanceledException(
                    "RetryContext's MaxElapsedTime budget was already exhausted before this " +
                    "attempt could start.");
            }

            attemptCts.CancelAfter(remaining);
        }

        _attempt++;

        var txn = await _inner.BeginTransactionAsync(cancellationToken: attemptCts.Token).ConfigureAwait(false);
        try
        {
            foreach (var template in _allContainers)
            {
                await using var clone = template.Clone(txn);
                var rowsAffected = await clone.ExecuteNonQueryAsync(CommandType.Text, attemptCts.Token)
                    .ConfigureAwait(false);
                EnforceRowCountPolicy(template, rowsAffected);
            }

            await txn.CommitAsync(attemptCts.Token).ConfigureAwait(false);
            _attempt = 0;
            _previousDelay = TimeSpan.Zero;
            completion.TrySetResult();
        }
        catch (DatabaseException ex) when (IsTransient(ex))
        {
            await SafeRollbackAsync(txn, attemptCts.Token).ConfigureAwait(false);

            // A transient failure at the commit itself is never safe to blindly retry as a whole
            // batch — unlike a mid-loop execution failure (rollback genuinely undid everything),
            // the transaction may have already committed server-side despite this exception, and
            // retrying would duplicate the entire batch's writes. Checked before the budget/
            // attempt-count gate below: "outcome unknown" is a strictly more useful signal than
            // "ran out of retries," so it takes priority even if this was the last allowed attempt.
            if (ex is TransactionException { Phase: TransactionPhase.Commit })
            {
                throw new RetryOutcomeUnknownException(ex, _attempt);
            }

            var budgetExceeded =
                Options.MaxElapsedTime is { } maxElapsed && _stopwatch!.Elapsed >= maxElapsed;
            if (_attempt >= Options.MaxAttempts || budgetExceeded)
            {
                throw;
            }

            _previousDelay = NextDelay(_previousDelay);
            ScheduleNextAttempt(_previousDelay);
        }
        catch
        {
            await SafeRollbackAsync(txn, attemptCts.Token).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await txn.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Best-effort rollback used when an attempt's transaction fails partway through. Skips the
    /// call entirely if the transaction is already <see cref="ITransactionContext.IsCompleted"/> —
    /// notably, a <c>CommitAsync</c> that itself throws already leaves the transaction completed
    /// (the connection released) per its own documented contract, so attempting a second
    /// commit/rollback on it is neither necessary nor safe. A rollback failure here is swallowed:
    /// the original exception that triggered the rollback is what must propagate, and there is
    /// nothing more this method could do about a rollback that itself fails.
    /// </summary>
    private static async ValueTask SafeRollbackAsync(ITransactionContext txn, CancellationToken cancellationToken)
    {
        if (txn.IsCompleted)
        {
            return;
        }

        try
        {
            await txn.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — see summary above.
        }
    }

    /// <summary>
    /// Re-arms the timer for the next step. A no-op if the run already concluded through some
    /// other path (e.g. external cancellation racing a just-finished attempt) — re-arming a
    /// finished run would resurrect it and risk a stray extra attempt after the caller already
    /// observed <see cref="StartAsync"/> complete.
    /// </summary>
    private void ScheduleNextAttempt(TimeSpan delay)
    {
        if (_completion?.Task.IsCompleted == true)
        {
            return;
        }

        var dueMs = delay <= TimeSpan.Zero ? 0L : (long)Math.Ceiling(delay.TotalMilliseconds);
        _timer?.Change(dueMs, Timeout.Infinite);
    }

    private bool IsTransient(DatabaseException ex) =>
        Options.IsTransientOverride?.Invoke(ex) ?? ex.IsTransient == true;

    /// <summary>
    /// Validates a just-executed command's affected-row count against the policy attached to its
    /// original queued template (if any — default is <see cref="RowCountPolicy.Ignore"/>). A
    /// violation is a data-integrity decision, not a database-error classification: it throws
    /// <see cref="RowCountPolicyViolationException"/>, which is deliberately not a
    /// <see cref="DatabaseException"/>, so it is never caught/retried by
    /// <see cref="RunStepAsync"/>'s transient-classification catch clause — it always
    /// propagates immediately and aborts execution. See "Per-command affected-row policy" in the
    /// design doc.
    /// </summary>
    private void EnforceRowCountPolicy(ISqlContainer template, int rowsAffected)
    {
        if (!_rowCountPolicies.TryGetValue(template, out var policy) || policy == RowCountPolicy.Ignore)
        {
            return;
        }

        var violated = policy switch
        {
            RowCountPolicy.AtLeastOne => rowsAffected < 1,
            RowCountPolicy.ExactlyOne => rowsAffected != 1,
            _ => false
        };

        if (violated)
        {
            throw new RowCountPolicyViolationException(policy, rowsAffected);
        }
    }

    /// <summary>
    /// Decorrelated exponential jitter: <c>sleep = min(cap, random_between(base, previous * 3))</c>
    /// (AWS Architecture Blog, "Exponential Backoff and Jitter," 2015). Returns
    /// <see cref="TimeSpan.Zero"/> when <see cref="RetryContextOptions.MaxDelay"/> is zero.
    /// </summary>
    private TimeSpan NextDelay(TimeSpan previous)
    {
        var baseMs = Math.Max(0, Options.BaseDelay.TotalMilliseconds);
        var capMs = Math.Max(baseMs, Options.MaxDelay.TotalMilliseconds);
        if (capMs <= 0)
        {
            return TimeSpan.Zero;
        }

        var prevMs = Math.Max(baseMs, previous.TotalMilliseconds);
        var upperMs = Math.Min(capMs, prevMs * 3);
        if (upperMs <= baseMs)
        {
            return TimeSpan.FromMilliseconds(baseMs);
        }

        var nextMs = baseMs + Random.Shared.NextDouble() * (upperMs - baseMs);
        return TimeSpan.FromMilliseconds(nextMs);
    }

    /// <summary>
    /// Builds a command against the plain parent context and appends it to this context's
    /// internal queue. Nothing executes here — see "Shape" in the design doc.
    /// </summary>
    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        ThrowIfDisposed();
        if (IsStarted)
        {
            throw new InvalidOperationException(
                "Cannot add commands to a RetryContext after StartAsync has been called; the " +
                "command plan is immutable once execution starts.");
        }

        var container = _inner.CreateSqlContainer(query);
        _queue.Enqueue(container);
        _allContainers.Add(container);
        return container;
    }

    // ---- Everything below is pure forwarding to the wrapped parent context. RetryContext does
    // not own a connection, pool, or dialect of its own — it defers entirely to whatever
    // IDatabaseContext it was constructed against. ----

    public DbMode ConnectionMode => _inner.ConnectionMode;

    public Guid RootId => _inner.RootId;

    public ReadWriteMode ReadWriteMode => _inner.ReadWriteMode;

    public string ConnectionString => _inner.ConnectionString;

    public string Name => _inner.Name;

    public IDataSourceInformation DataSourceInfo => _inner.DataSourceInfo;

    public TimeSpan? ModeLockTimeout => _inner.ModeLockTimeout;

    public ProcWrappingStyle ProcWrappingStyle => _inner.ProcWrappingStyle;

    public int MaxParameterLimit => _inner.MaxParameterLimit;

    public int MaxOutputParameters => _inner.MaxOutputParameters;

    public long NumberOfOpenConnections => _inner.NumberOfOpenConnections;

    public DatabaseMetrics Metrics => _inner.Metrics;

    public PoolStatisticsSnapshot GetPoolStatisticsSnapshot(PoolLabel label) =>
        _inner.GetPoolStatisticsSnapshot(label);

    public event EventHandler<DatabaseMetrics> MetricsUpdated
    {
        add => _inner.MetricsUpdated += value;
        remove => _inner.MetricsUpdated -= value;
    }

    public ISqlDialect Dialect => _inner.Dialect;

    public SupportedDatabase Product => _inner.Product;

    public long PeakOpenConnections => _inner.PeakOpenConnections;

    public int? ReaderPlanCacheSize => _inner.ReaderPlanCacheSize;

    public CommandPrepareMode PrepareMode => _inner.PrepareMode;

    public bool SupportsInsertReturning => _inner.SupportsInsertReturning;

    public string QuotePrefix => _inner.QuotePrefix;

    public string QuoteSuffix => _inner.QuoteSuffix;

    public string CompositeIdentifierSeparator => _inner.CompositeIdentifierSeparator;

    public string WrapObjectName(string name) => _inner.WrapObjectName(name);

    public string MakeParameterName(DbParameter dbParameter) => _inner.MakeParameterName(dbParameter);

    public string MakeParameterName(string parameterName) => _inner.MakeParameterName(parameterName);

    public bool IsReadOnlyConnection => _inner.IsReadOnlyConnection;

    public bool RCSIEnabled => _inner.RCSIEnabled;

    public string GetBaseSessionSettings() => _inner.GetBaseSessionSettings();

    public string GetReadOnlySessionSettings() => _inner.GetReadOnlySessionSettings();

    public bool SnapshotIsolationEnabled => _inner.SnapshotIsolationEnabled;

    public IReadOnlySet<IsolationLevel> GetSupportedIsolationLevels() => _inner.GetSupportedIsolationLevels();

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value) =>
        _inner.CreateDbParameter(name, type, value);

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value, ParameterDirection direction) =>
        _inner.CreateDbParameter(name, type, value, direction);

    public DbParameter CreateDbParameter<T>(DbType type, T value) => _inner.CreateDbParameter(type, value);

    public string GenerateParameterName() => _inner.GenerateParameterName();

    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30) =>
        _inner.GenerateRandomName(length, parameterNameMaxLength);

    // ---- Transactions are deliberately not forwarded. RetryContext owns its own transaction
    // lifecycle internally (per RetryContextType, inside StartAsync) — a caller-initiated
    // transaction on top of that would conflict with it, the same way ITransactionContext already
    // forbids nesting. See shortcoming #3 in the design doc. ----

    public ITransactionContext BeginTransaction(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write) =>
        throw new NotSupportedException(
            "RetryContext owns its own transaction lifecycle internally and does not support " +
            "caller-initiated transactions. Queue commands via CreateSqlContainer/BuildX and call " +
            "StartAsync instead.");

    public ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write,
        IsolationResolutionPolicy policy = IsolationResolutionPolicy.AllowHigher) =>
        throw new NotSupportedException(
            "RetryContext owns its own transaction lifecycle internally and does not support " +
            "caller-initiated transactions. Queue commands via CreateSqlContainer/BuildX and call " +
            "StartAsync instead.");

    public ValueTask<ITransactionContext> BeginTransactionAsync(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "RetryContext owns its own transaction lifecycle internally and does not support " +
            "caller-initiated transactions. Queue commands via CreateSqlContainer/BuildX and call " +
            "StartAsync instead.");

    public ValueTask<ITransactionContext> BeginTransactionAsync(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write,
        CancellationToken cancellationToken = default,
        IsolationResolutionPolicy policy = IsolationResolutionPolicy.AllowHigher) =>
        throw new NotSupportedException(
            "RetryContext owns its own transaction lifecycle internally and does not support " +
            "caller-initiated transactions. Queue commands via CreateSqlContainer/BuildX and call " +
            "StartAsync instead.");

    // ---- Disposal: RetryContext does not own the wrapped parent context (it is caller-owned —
    // typically the same DatabaseContext singleton used everywhere else) and must never dispose
    // it. It DOES own every ISqlContainer it ever built via CreateSqlContainer — regardless of
    // whether that command ever ran, succeeded, or is still sitting in the queue — and releases
    // all of them here, once, rather than piecemeal as each attempt finishes. Containers don't
    // hold a live connection at rest (only a brief per-attempt Clone() does, and that's disposed
    // immediately after each attempt in RunSequentialAttemptAsync/RunTransactionalAttemptAsync),
    // so deferring their disposal to this single point costs nothing in practice.
    //
    // Lifetime hazard this guards against: if Dispose/DisposeAsync is called while StartAsync is
    // still actually running (a caller bug — StartAsync should always be awaited to completion
    // before disposing), it is not safe to enumerate/dispose _queue/_allContainers concurrently
    // with RunStepAsync's callback chain mutating them. IsStarted && !IsCompleted is exactly that
    // "still running" window, so both paths below check it first and throw loudly instead of
    // silently leaving orphaned background work or corrupting the collections — see "Required
    // implementation corrections" in the design doc ("must not silently convert failure into
    // success").
    //
    // Both attempt methods now roll back (never commit) on any failure path before disposing their
    // per-attempt transaction — see SafeRollbackAsync — so "Disposal never intentionally commits"
    // (the design doc's commit-ambiguity note) already holds for the transactions RetryContext
    // itself opens; this Dispose only ever tears down queued ISqlContainer templates, not a live
    // transaction. ----
    protected override void DisposeManaged()
    {
        if (IsStarted && !IsCompleted)
        {
            _stopCts.Dispose();
            throw new InvalidOperationException(
                "RetryContext was disposed while StartAsync was still running. Await StartAsync to " +
                "completion before disposing — queued commands were left untouched because it is " +
                "not safe to enumerate them while StartAsync is concurrently executing.");
        }

        foreach (var container in _allContainers)
        {
            container.Dispose();
        }

        _stopCts.Dispose();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (IsStarted && !IsCompleted)
        {
            _stopCts.Dispose();
            throw new InvalidOperationException(
                "RetryContext was disposed while StartAsync was still running. Await StartAsync to " +
                "completion before disposing — queued commands were left untouched because it is " +
                "not safe to enumerate them while StartAsync is concurrently executing.");
        }

        foreach (var container in _allContainers)
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }

        _stopCts.Dispose();
    }
}
