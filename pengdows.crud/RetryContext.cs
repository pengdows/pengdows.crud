// =============================================================================
// FILE: RetryContext.cs
// PURPOSE: RetryContext subsystem (FEAT-001) — see docs/planning/retry-context-design.md for the
//          full design.
//
// STATUS: Sequential executes one queued command per transaction; Transactional executes the
// entire queue atomically. Execution failures are retried only after the attempt's transaction
// has been rolled back. Commit failures and cancellation after commit begins are treated as
// outcome-unknown and are never replayed blindly. Retry safety for custom commands is explicit
// metadata; RetryContext does not infer semantic safety from SQL text.
//
// EXECUTION MODEL: Attempts are serialized. Each next attempt starts only after the current
// attempt's transaction disposal has completed, and cancellation cannot complete StartAsync before
// that cleanup barrier.
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
    private readonly CancellationTokenSource _stopCts = new();
    private int _started;
    private int _completed;

    // Run state is owned by the single awaited executor loop; no timer callbacks or concurrent
    // attempt-state mutations are involved.
    private TaskCompletionSource? _completion;
    private Stopwatch? _stopwatch;
    private TimeSpan _previousDelay;
    private TimeSpan? _nextDelay;
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
        ValidateOptions(Options);
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
        ThrowIfStarted();
        ArgumentNullException.ThrowIfNull(container);
        if (!_queue.Contains(container))
        {
            throw new ArgumentException(
                "The container is not part of this RetryContext's queue.", nameof(container));
        }

        _rowCountPolicies[container] = policy;
    }

    private void ThrowIfStarted()
    {
        if (IsStarted)
        {
            throw new InvalidOperationException(
                "RetryContext configuration cannot be changed after StartAsync has been called.");
        }
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
            await RunRetryLoopAsync(cancellationToken).ConfigureAwait(false);
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
    /// Executes attempts serially and waits for each transaction disposal to complete before
    /// applying the next backoff and starting another attempt.
    /// </summary>
    private async ValueTask RunRetryLoopAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        _stopwatch = Stopwatch.StartNew();
        _attempt = 0;
        _previousDelay = TimeSpan.Zero;
        _nextDelay = null;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;

        try
        {
            while (!completion.Task.IsCompleted)
            {
                _nextDelay = null;
                await RunStepAsync(linked.Token).ConfigureAwait(false);

                if (!completion.Task.IsCompleted && _nextDelay is { } nextDelay)
                {
                    await Task.Delay(nextDelay, linked.Token).ConfigureAwait(false);
                }
            }

            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _completion = null;
        }
    }

    private async Task RunStepAsync(CancellationToken cancellationToken)
    {
        var completion = _completion!;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RetryContextType == RetryContextType.Sequential)
            {
                await RunSequentialAttemptAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunTransactionalAttemptAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
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
    /// fails transiently is retried in place, after a *confirmed* rollback, up to
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
    /// Commit-ambiguity handling (shortcoming #1) is implemented via the confirmed-rollback
    /// guarantee, not statement-shape inference: <see cref="RollbackOrThrowOutcomeUnknownAsync"/>
    /// fails closed with <see cref="RetryOutcomeUnknownException"/> whenever rollback itself
    /// cannot be confirmed, so a retry only ever happens once rollback for the failed attempt has
    /// genuinely completed — at which point nothing from that attempt could have durably applied,
    /// regardless of whether the command was a <c>DELETE</c>, a bare <c>INSERT</c>, or anything
    /// else. There is deliberately no per-statement-shape classification here (an earlier revision
    /// had one, gated on <c>[Version]</c>-guarded updates and an opt-in
    /// <c>RetrySafety.IdempotentViaUniqueConstraint</c> declaration; both were removed once
    /// wrapping each command in its own transaction with confirmed rollback made the distinction
    /// unnecessary — a unique-constraint violation on a later attempt can only mean a genuinely
    /// separate conflict, since a confirmed rollback rules out this same command's own earlier
    /// attempt having landed). A <c>CommitAsync</c> that itself throws transiently is separately
    /// handled, the same way as in <see cref="RunTransactionalAttemptAsync"/>: a
    /// <see cref="TransactionException"/> whose <see cref="TransactionException.Phase"/> is
    /// <see cref="TransactionPhase.Commit"/> fails closed with
    /// <see cref="RetryOutcomeUnknownException"/> unconditionally rather than being retried.
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

        ITransactionContext? txn = null;
        var commitStarted = false;
        try
        {
            txn = await _inner.BeginTransactionAsync(cancellationToken: attemptCts.Token).ConfigureAwait(false);
            await using var clone = template.Clone(txn);
            var rowsAffected = await clone.ExecuteNonQueryAsync(CommandType.Text, attemptCts.Token)
                .ConfigureAwait(false);
            EnforceRowCountPolicy(template, rowsAffected);

            commitStarted = true;
            await txn.CommitAsync(attemptCts.Token).ConfigureAwait(false);

            _queue.Dequeue();
            _attempt = 0;
            _previousDelay = TimeSpan.Zero;
            _nextDelay = TimeSpan.Zero;
        }
        catch (TransactionException ex) when (commitStarted || ex.Phase == TransactionPhase.Commit)
        {
            if (txn is not null)
            {
                await RollbackOrThrowOutcomeUnknownAsync(txn, ex, attemptCts.Token).ConfigureAwait(false);
            }

            throw new RetryOutcomeUnknownException(ex, _attempt);
        }
        catch (OperationCanceledException ex) when (commitStarted && txn?.IsCompleted == true)
        {
            throw new RetryOutcomeUnknownException(ex, _attempt);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            if (txn is not null)
            {
                await RollbackOrThrowOutcomeUnknownAsync(txn, ex, attemptCts.Token).ConfigureAwait(false);
            }

            var budgetExceeded =
                Options.MaxElapsedTime is { } maxElapsed && _stopwatch!.Elapsed >= maxElapsed;
            if (_attempt >= Options.MaxAttempts || budgetExceeded)
            {
                throw;
            }

            _previousDelay = NextDelay(_previousDelay);
            _nextDelay = _previousDelay;
        }
        catch (Exception ex)
        {
            if (txn is not null)
            {
                await RollbackOrThrowOutcomeUnknownAsync(txn, ex, attemptCts.Token).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            if (txn is not null)
            {
                await txn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

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

        ITransactionContext? txn = null;
        var commitStarted = false;
        try
        {
            txn = await _inner.BeginTransactionAsync(cancellationToken: attemptCts.Token).ConfigureAwait(false);
            foreach (var template in _allContainers)
            {
                await using var clone = template.Clone(txn);
                var rowsAffected = await clone.ExecuteNonQueryAsync(CommandType.Text, attemptCts.Token)
                    .ConfigureAwait(false);
                EnforceRowCountPolicy(template, rowsAffected);
            }

            commitStarted = true;
            await txn.CommitAsync(attemptCts.Token).ConfigureAwait(false);
            _attempt = 0;
            _previousDelay = TimeSpan.Zero;
            _queue.Clear();
            completion.TrySetResult();
        }
        catch (TransactionException ex) when (commitStarted || ex.Phase == TransactionPhase.Commit)
        {
            throw new RetryOutcomeUnknownException(ex, _attempt);
        }
        catch (OperationCanceledException ex) when (commitStarted && txn?.IsCompleted == true)
        {
            throw new RetryOutcomeUnknownException(ex, _attempt);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            if (txn is not null)
            {
                await RollbackOrThrowOutcomeUnknownAsync(txn, ex, attemptCts.Token).ConfigureAwait(false);
            }

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
            _nextDelay = _previousDelay;
        }
        catch (Exception ex)
        {
            if (txn is not null)
            {
                await RollbackOrThrowOutcomeUnknownAsync(txn, ex, attemptCts.Token).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            if (txn is not null)
            {
                await txn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Rolls back an attempt. If rollback cannot be confirmed, execution fails closed with an
    /// outcome-unknown exception preserving both the original failure and rollback failure.
    /// </summary>
    private async ValueTask RollbackOrThrowOutcomeUnknownAsync(
        ITransactionContext txn, Exception original, CancellationToken cancellationToken)
    {
        if (txn.IsCompleted)
        {
            return;
        }

        try
        {
            await txn.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception rollbackFailure)
        {
            throw new RetryOutcomeUnknownException(
                new AggregateException("The operation failed and rollback could not be confirmed.", original, rollbackFailure),
                _attempt);
        }
    }

    /// <summary>
    /// <see cref="IExecutionGovernanceRejection"/> (PoolSaturatedException/ModeContentionException/
    /// PoolForbiddenException) is checked first and unconditionally — per the design's "never
    /// retried, full stop" policy, no <see cref="RetryContextOptions.IsTransientOverride"/> may
    /// reclassify one of these three as retryable. <c>PoolGovernor</c> already owns bounded
    /// waiting/backpressure for admission rejections; a second, uncoordinated retry on top would
    /// add load exactly when the governor decided to stop.
    /// </summary>
    private bool IsTransient(Exception ex) =>
        ex is not IExecutionGovernanceRejection &&
        ex is DatabaseException db &&
        (Options.IsTransientOverride?.Invoke(db) ?? db.IsTransient == true);

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
    // still actually running, it is not safe to enumerate/dispose _queue/_allContainers
    // concurrently with RunStepAsync's callback chain. ValidateDispose checks this before the
    // base class marks the object disposed, so a rejected disposal remains retryable after the run
    // reaches its terminal state.
    //
    // Both attempt methods now roll back (never commit) on any failure path before disposing their
    // per-attempt transaction — see RollbackOrThrowOutcomeUnknownAsync — so "Disposal never intentionally commits"
    // (the design doc's commit-ambiguity note) already holds for the transactions RetryContext
    // itself opens; this Dispose only ever tears down queued ISqlContainer templates, not a live
    // transaction. ----
    protected override void DisposeManaged()
    {
        foreach (var container in _allContainers)
        {
            container.Dispose();
        }

        _stopCts.Dispose();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        foreach (var container in _allContainers)
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }

        _stopCts.Dispose();
    }

    protected override void ValidateDispose()
    {
        if (IsStarted && !IsCompleted)
        {
            throw new InvalidOperationException(
                "RetryContext cannot be disposed while StartAsync is running. Await StartAsync to " +
                "completion before disposing.");
        }
    }

    private static void ValidateOptions(RetryContextOptions options)
    {
        if (options.MaxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxAttempts must be positive.");
        }

        if (options.BaseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BaseDelay cannot be negative.");
        }

        if (options.MaxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDelay cannot be negative.");
        }

        if (options.MaxElapsedTime is { } elapsed && elapsed <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxElapsedTime must be positive.");
        }
    }
}
