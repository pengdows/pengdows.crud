// =============================================================================
// FILE: RetryContext.cs
// PURPOSE: RetryContext subsystem (FEAT-001) — see docs/planning/retry-context-design.md for the
//          full design.
//
// STATUS: RetryContextType.Sequential is implemented (FIFO execution against the plain parent
// context, DatabaseException.IsTransient-based retry with decorrelated exponential jitter backoff,
// per-command RowCountPolicy enforcement) per "Dual retry modes" and "Per-command affected-row
// policy" in the design doc. RetryContextType.Transactional is NOT implemented — see its
// NotImplementedException below. Commit-ambiguity statement-shape detection (shortcoming #1) is
// NOT implemented — every transient failure is currently treated as safe to retry, which is only
// actually decided-safe for DELETE and version-guarded UPDATE per the design doc; this is a known
// gap, not a decided relaxation of that policy.
//
// EXECUTION MODEL: Sequential is timer-driven, not one continuous awaited loop. StartAsync arms a
// one-shot System.Threading.Timer for the first attempt and returns only once a
// TaskCompletionSource the timer callback chain resolves. Every firing disarms the timer FIRST
// (Change(Infinite, Infinite)) before doing anything else, and only re-arms it once that attempt's
// outcome is fully decided — the classic "stop the timer on entry, conditionally re-enable it on
// exit" discipline, so attempt N+1 (or its backoff wait) can never start while attempt N is still
// actually in flight.
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

    // Sequential timer-driven run state — only ever touched by one attempt's worth of callback
    // chain at a time (see the class-level "EXECUTION MODEL" note above), so plain fields are
    // safe: nothing else can be concurrently mutating them while a run is in flight.
    private Timer? _sequentialTimer;
    private TaskCompletionSource? _sequentialCompletion;
    private Stopwatch? _sequentialStopwatch;
    private TimeSpan _sequentialPreviousDelay;
    private int _sequentialAttempt;

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

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "StartAsync has already been called on this RetryContext; only one executor may " +
                "run a given context.");
        }

        try
        {
            switch (RetryContextType)
            {
                case RetryContextType.Sequential:
                    await RunSequentialTimerDrivenAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case RetryContextType.Transactional:
                    // TODO(FEAT-001): not implemented. See "Execution mechanism," "Dual retry
                    // modes," shortcoming #1 (commit ambiguity), and "Implementation-correctness
                    // corrections" in docs/planning/retry-context-design.md.
                    throw new NotImplementedException(
                        "RetryContext.StartAsync for RetryContextType.Transactional is not " +
                        "implemented yet — see docs/planning/retry-context-design.md.");
                default:
                    throw new NotSupportedException($"Unknown RetryContextType: {RetryContextType}");
            }
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
    /// RetryContextType.Sequential: commands execute one at a time against the plain parent
    /// context, no transaction involved. A command that succeeds is removed from the queue and
    /// never re-executed; a command that fails transiently is retried in place, up to
    /// <see cref="RetryContextOptions.MaxAttempts"/>/<see cref="RetryContextOptions.MaxElapsedTime"/>.
    /// See "Dual retry modes" in the design doc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Timer-driven, not one continuous awaited loop — see the class-level "EXECUTION MODEL" note.
    /// This method only arms the first attempt and returns a <see cref="ValueTask"/> that
    /// completes once <see cref="RunSequentialStepAsync"/>'s callback chain resolves
    /// <c>_sequentialCompletion</c>; it does not itself execute anything.
    /// </para>
    /// <para>
    /// KNOWN GAP: statement-shape commit-ambiguity detection (shortcoming #1) is not implemented.
    /// Every DatabaseException classified transient is retried unconditionally here — the design
    /// doc only actually decides that's safe for DELETE and version-guarded UPDATE. Do not treat
    /// this as implementing the design's full idempotency story yet.
    /// </para>
    /// </remarks>
    private ValueTask RunSequentialTimerDrivenAsync(CancellationToken cancellationToken) =>
        RunTimerDrivenAsync(cancellationToken, OnSequentialTimerFired);

    /// <summary>
    /// Shared timer-arming scaffolding for both retry modes — see the class-level "EXECUTION
    /// MODEL" note. The only thing that varies between <see cref="RetryContextType.Sequential"/>
    /// and (once implemented) <see cref="RetryContextType.Transactional"/> is which callback the
    /// timer fires; everything else (linked-token setup, run-state reset, the completion source,
    /// cancellation wiring, arming the first attempt) is identical, so it lives here once.
    /// </summary>
    private ValueTask RunTimerDrivenAsync(CancellationToken cancellationToken, TimerCallback onTimerFired)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        _sequentialStopwatch = Stopwatch.StartNew();
        _sequentialAttempt = 0;
        _sequentialPreviousDelay = TimeSpan.Zero;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sequentialCompletion = completion;

        // Responds to cancellation immediately, independent of whatever the timer is currently
        // doing — without this, a cancel requested while the timer is idle mid-backoff wouldn't be
        // noticed until that timer eventually fired.
        var cancelRegistration = linked.Token.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            completion);

        _sequentialTimer = new Timer(onTimerFired, linked.Token, Timeout.Infinite, Timeout.Infinite);

        // The only place StartAsync itself schedules anything — every attempt after this one is
        // scheduled by the timer callback chain below, not by this method.
        ScheduleNextSequentialAttempt(TimeSpan.Zero);

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
            _sequentialTimer?.Dispose();
            _sequentialTimer = null;
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
    private void OnSequentialTimerFired(object? state)
    {
        try
        {
            _sequentialTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _ = RunSequentialStepAsync((CancellationToken)state!);
        }
        catch (ObjectDisposedException)
        {
            // A callback that was already queued raced RetryContext disposing the timer after the
            // run concluded through some other path. The run is over either way — nothing to do.
        }
    }

    /// <summary>
    /// Executes exactly one attempt of the current queue head, then either resolves
    /// <c>_sequentialCompletion</c> (queue empty / non-transient or budget-exhausted failure /
    /// cancellation) or re-arms the timer for the next step (success moving to the next command,
    /// or a transient failure's backoff wait) and returns. Never throws past its own boundary —
    /// every exception is funneled into <c>_sequentialCompletion</c> instead, since this runs as a
    /// timer callback's fire-and-forget continuation, not something anyone awaits directly.
    /// </summary>
    private async Task RunSequentialStepAsync(CancellationToken cancellationToken)
    {
        var completion = _sequentialCompletion!;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                var remaining = budget - _sequentialStopwatch!.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new OperationCanceledException(
                        "RetryContext's MaxElapsedTime budget was already exhausted before this " +
                        "attempt could start.");
                }

                attemptCts.CancelAfter(remaining);
            }

            _sequentialAttempt++;

            await using var clone = template.Clone();
            try
            {
                var rowsAffected = await clone.ExecuteNonQueryAsync(CommandType.Text, attemptCts.Token)
                    .ConfigureAwait(false);
                EnforceRowCountPolicy(template, rowsAffected);
                _queue.Dequeue();
                _sequentialAttempt = 0;
                _sequentialPreviousDelay = TimeSpan.Zero;
                ScheduleNextSequentialAttempt(TimeSpan.Zero);
            }
            catch (DatabaseException ex) when (IsTransient(ex))
            {
                var budgetExceeded =
                    Options.MaxElapsedTime is { } maxElapsed && _sequentialStopwatch!.Elapsed >= maxElapsed;
                if (_sequentialAttempt >= Options.MaxAttempts || budgetExceeded)
                {
                    throw;
                }

                _sequentialPreviousDelay = NextDelay(_sequentialPreviousDelay);
                ScheduleNextSequentialAttempt(_sequentialPreviousDelay);
            }
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    /// <summary>
    /// Re-arms the timer for the next step. A no-op if the run already concluded through some
    /// other path (e.g. external cancellation racing a just-finished attempt) — re-arming a
    /// finished run would resurrect it and risk a stray extra attempt after the caller already
    /// observed <see cref="StartAsync"/> complete.
    /// </summary>
    private void ScheduleNextSequentialAttempt(TimeSpan delay)
    {
        if (_sequentialCompletion?.Task.IsCompleted == true)
        {
            return;
        }

        var dueMs = delay <= TimeSpan.Zero ? 0L : (long)Math.Ceiling(delay.TotalMilliseconds);
        _sequentialTimer?.Change(dueMs, Timeout.Infinite);
    }

    private bool IsTransient(DatabaseException ex) =>
        Options.IsTransientOverride?.Invoke(ex) ?? ex.IsTransient == true;

    /// <summary>
    /// Validates a just-executed command's affected-row count against the policy attached to its
    /// original queued template (if any — default is <see cref="RowCountPolicy.Ignore"/>). A
    /// violation is a data-integrity decision, not a database-error classification: it throws
    /// <see cref="RowCountPolicyViolationException"/>, which is deliberately not a
    /// <see cref="DatabaseException"/>, so it is never caught/retried by
    /// <see cref="RunSequentialStepAsync"/>'s transient-classification catch clause — it always
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
    // immediately after each attempt in RunSequentialAsync), so deferring their disposal to this
    // single point costs nothing in practice.
    //
    // Lifetime hazard this guards against: if Dispose/DisposeAsync is called while StartAsync is
    // still actually running (a caller bug — StartAsync should always be awaited to completion
    // before disposing), it is not safe to enumerate/dispose _queue/_allContainers concurrently
    // with RunSequentialAsync mutating them. IsStarted && !IsCompleted is exactly that "still
    // running" window, so both paths below check it first and throw loudly instead of silently
    // leaving orphaned background work or corrupting the collections — see "Required
    // implementation corrections" in the design doc ("must not silently convert failure into
    // success").
    //
    // TODO(FEAT-001): once RetryContextType.Transactional owns a live transaction attempt,
    // disposal must roll it back, never commit it — see "Commit ambiguity" in the design doc:
    // "Disposal never intentionally commits." No such state exists yet (Sequential mode never
    // opens a transaction — see "Execution mechanism" in the design doc). ----
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
