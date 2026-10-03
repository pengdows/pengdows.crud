// =============================================================================
// FILE: TenantContextRegistry.cs
// PURPOSE: Registry managing DatabaseContext instances per tenant.
//
// AI SUMMARY:
// - Implements ITenantContextRegistry for tenant context lifecycle management.
// - Thread-safe: ConcurrentDictionary<string, TenantContextEntry>, case-insensitive keys
//   (matches TenantConnectionResolver's StringComparer.OrdinalIgnoreCase — "foo"/"FOO" resolve
//   to exactly one cached context, not two independently governed ones).
// - Each entry wraps a Lazy<IDatabaseContext> (single-flight construction) plus a CAS-based
//   lease refcount that lets AcquireLease defer Invalidate's disposal until every outstanding
//   lease releases — see TenantContextEntry below.
// - GetContext(tenant): bare-reference read; still subject to the historical residual race
//   against a concurrent Invalidate (no lease taken) — use AcquireLease for protection.
// - AcquireLease(tenant): reference-counted lease API. A leased context is guaranteed not to be
//   disposed by Invalidate/InvalidateAll until the lease itself is disposed.
// - Invalidate(tenant): removes the tenant's entry; disposal is immediate if idle (no leases),
//   deferred to the last lease's release otherwise. Never blocks the calling thread — actual
//   disposal is dispatched onto the thread pool (ScheduleDisposeEntry), since DatabaseContext
//   disposal can itself block on its own pool-governor drain wait.
// - InvalidateAll(): evicts all cached contexts; next GetContext/AcquireLease recreates each.
// - Optional MaxTenantCount cap: admission is atomic against the dictionary check-and-add
//   (_admissionLock), closing a TOCTOU race that could otherwise let concurrent distinct
//   tenants exceed the cap.
// - Extends SafeAsyncDisposableBase for proper cleanup.
// - DisposeManaged: disposes every already-created context inline; an entry still under
//   construction (in flight on another thread) is handed to a background work item that blocks
//   on Lazy<IDatabaseContext>.Value, then disposes the result — never leaked, and never blocks
//   Dispose() itself.
// - DisposeManagedAsync: same, except an in-flight construction is awaited (via the entry's
//   non-blocking completion signal, not Lazy.Value) and disposed before DisposeAsync completes.
// =============================================================================

using System.Collections.Concurrent;
using System.Data.Common;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.tenant;

/// <summary>
/// Thread-safe registry of <see cref="IDatabaseContext"/> instances keyed by tenant identifier.
/// </summary>
/// <remarks>
/// <para>
/// Each tenant context is created lazily on first access and cached for subsequent calls.
/// <see cref="Invalidate"/> or <see cref="InvalidateAll"/> can be used to evict stale
/// contexts when tenant configuration changes:
/// </para>
/// <list type="number">
///   <item>Update the tenant's configuration via <c>TenantConnectionResolver.Register</c>.</item>
///   <item>Call <see cref="Invalidate"/> (or <see cref="InvalidateAll"/>) to evict the stale context.</item>
///   <item>The next <see cref="GetContext"/> call creates a fresh context using the new configuration.</item>
/// </list>
/// <para>
/// <b>Disposal:</b> After this registry is disposed, <see cref="GetContext"/> and <see cref="AcquireLease"/> throw
/// <see cref="ObjectDisposedException"/>. Contexts obtained before disposal continue working
/// normally until they are themselves disposed.
/// </para>
/// <para>
/// <b>Cardinality:</b> The optional <c>maxTenantCount</c> constructor parameter enforces an upper
/// bound on distinct tenants. Unbounded registries in long-lived apps with many tenants can cause
/// connection-pool explosion; call <see cref="InvalidateAll"/> or use the cap accordingly.
/// </para>
/// <para>
/// <b>Live rotation:</b> <see cref="Invalidate"/>/<see cref="InvalidateAll"/> combined with
/// <see cref="AcquireLease"/> make live tenant ejection/rotation a safe, supported pattern — a
/// leased context survives a concurrent rotation until the lease is released. <see cref="GetContext"/>
/// remains the simpler, bare-reference option for the common "resolve, then immediately use" case.
/// </para>
/// </remarks>
public class TenantContextRegistry : SafeAsyncDisposableBase, ITenantContextRegistry
{
    /// <summary>
    /// A single tenant's cached construction + lease state.
    /// </summary>
    /// <remarks>
    /// <see cref="_leaseCount"/> doubles as the exactly-once disposal guard: <see cref="Dead"/> is
    /// a reserved sentinel meaning "disposal already committed." <see cref="TryAddLease"/> is a CAS
    /// loop rather than a plain increment specifically because a plain increment cannot detect
    /// "someone already committed to disposing this" — it would let a lease resurrect a reference
    /// to an already-disposed context. Whichever of <see cref="ReleaseLease"/> or
    /// <see cref="MarkRemoved"/> wins the CAS transition from 0 to <see cref="Dead"/> is the sole
    /// owner of calling <see cref="TenantContextRegistry.ScheduleDisposeEntry"/>.
    /// <para>
    /// That guard alone does not cover the registry's own shutdown path
    /// (<see cref="TenantContextRegistry.DisposeManaged"/>/<see cref="TenantContextRegistry.DisposeManagedAsync"/>),
    /// which disposes unconditionally by design. Its <c>_contexts.Values.ToArray()</c> snapshot can
    /// observe an entry a concurrent <see cref="MarkRemoved"/> has already committed to disposing.
    /// <see cref="_disposalClaimed"/> is a separate one-time guard: whichever disposal path calls
    /// <see cref="TryClaimDisposal"/> first disposes the context and fires <c>ContextRemoved</c>;
    /// every other caller skips.
    /// </para>
    /// </remarks>
    internal sealed class TenantContextEntry
    {
        private const int Dead = int.MinValue;

        private int _leaseCount;
        private int _removed;
        private int _disposalClaimed;

        /// <summary>
        /// Atomically claims the right to be the sole caller that disposes this entry's context
        /// and fires <c>ContextRemoved</c> for it. Returns true for exactly one caller, ever.
        /// </summary>
        public bool TryClaimDisposal() => Interlocked.CompareExchange(ref _disposalClaimed, 1, 0) == 0;

        /// <summary>
        /// Single-flight construction shared by sync and async callers (BP-311). The factory is the
        /// one chosen by whichever caller admitted the entry: a synchronous caller constructs
        /// synchronously inside the Lazy (as before, returning a completed task), an asynchronous
        /// caller only starts <see cref="IDatabaseContextFactory.CreateAsync"/>. So a sync caller
        /// never blocks on sync-over-async it started itself.
        /// </summary>
        public Lazy<Task<IDatabaseContext>> LazyContext { get; }

        // Completed (never faulted) when construction finishes: the context on success, null on
        // failure. Lets DisposeManagedAsync await an in-flight construction without blocking any
        // thread on Lazy<T>.Value (which blocks synchronously while another thread evaluates it).
        private readonly TaskCompletionSource<IDatabaseContext?> _constructed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _constructionStarted;

        public Task<IDatabaseContext?> Constructed => _constructed.Task;

        public bool ConstructionStarted => Volatile.Read(ref _constructionStarted) != 0;

        public TenantContextEntry(Func<Task<IDatabaseContext>> factory)
        {
            LazyContext = new Lazy<Task<IDatabaseContext>>(() =>
            {
                Interlocked.Exchange(ref _constructionStarted, 1);
                Task<IDatabaseContext> task;
                try
                {
                    task = factory();
                }
                catch (Exception ex)
                {
                    task = Task.FromException<IDatabaseContext>(ex);
                }

                task.ContinueWith(
                    static (completed, state) => ((TaskCompletionSource<IDatabaseContext?>)state!)
                        .TrySetResult(completed.IsCompletedSuccessfully ? completed.Result : null),
                    _constructed, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return task;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// Attempts to add a lease. Fails (returns false) if this entry already committed to
        /// disposal — the caller must retry against a freshly resolved/created entry.
        /// </summary>
        public bool TryAddLease()
        {
            while (true)
            {
                var current = Volatile.Read(ref _leaseCount);
                if (current == Dead)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _leaseCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Releases a previously-added lease. If this was the last outstanding lease and the
        /// entry has since been removed via <see cref="MarkRemoved"/>, schedules its disposal.
        /// </summary>
        public void ReleaseLease(TenantContextRegistry owner, string tenant)
        {
            var next = Interlocked.Decrement(ref _leaseCount);
            if (next == 0 && Volatile.Read(ref _removed) != 0)
            {
                if (Interlocked.CompareExchange(ref _leaseCount, Dead, 0) == 0)
                {
                    owner.ScheduleDisposeEntry(tenant, this);
                }
                // Else: a concurrent TryAddLease grabbed a fresh lease before we could claim
                // disposal ownership — no longer idle, defer to that lease's eventual release.
            }
        }

        /// <summary>
        /// Marks this entry as removed from the registry's lookup dictionary. If idle (no
        /// outstanding leases) at this instant, schedules disposal immediately. Otherwise defers to
        /// whichever <see cref="ReleaseLease"/> call eventually brings the count to zero.
        /// </summary>
        public void MarkRemoved(TenantContextRegistry owner, string tenant)
        {
            Interlocked.Exchange(ref _removed, 1);
            if (Interlocked.CompareExchange(ref _leaseCount, Dead, 0) == 0)
            {
                owner.ScheduleDisposeEntry(tenant, this);
            }
        }
    }

    // Case-insensitive to match TenantConnectionResolver's tenant-ID comparison
    // (StringComparer.OrdinalIgnoreCase) — "tenant-x" and "TENANT-X" must resolve to exactly
    // one cached context, not two independently governed ones.
    private readonly ConcurrentDictionary<string, TenantContextEntry> _contexts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _admissionLock = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionResolver _resolver;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IDatabaseContextFactory _contextFactory;
    private readonly int? _maxTenantCount;

    public event Action<IDatabaseContext>? ContextCreated;
    public event Action<IDatabaseContext>? ContextRemoved;

    /// <param name="serviceProvider">DI service provider used to resolve keyed <see cref="DbProviderFactory"/> instances.</param>
    /// <param name="resolver">Maps tenant identifiers to their database configurations.</param>
    /// <param name="contextFactory">Factory used to construct <see cref="IDatabaseContext"/> instances.</param>
    /// <param name="loggerFactory">Logger factory for the registry and created contexts.</param>
    /// <param name="maxTenantCount">
    /// Optional upper bound on distinct cached tenants. When set and the limit is reached,
    /// <see cref="GetContext"/>, <see cref="GetContextAsync"/>, <see cref="AcquireLease"/> and
    /// <see cref="AcquireLeaseAsync"/> throw <see cref="InvalidOperationException"/> for new tenants.
    /// Call <see cref="Invalidate"/> or <see cref="InvalidateAll"/> to evict unused entries.
    /// </param>
    public TenantContextRegistry(
        IServiceProvider serviceProvider,
        ITenantConnectionResolver resolver,
        IDatabaseContextFactory contextFactory,
        ILoggerFactory loggerFactory,
        int? maxTenantCount = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<TenantContextRegistry>();

        if (maxTenantCount.HasValue && maxTenantCount.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTenantCount),
                "maxTenantCount must be greater than zero when specified.");
        }

        _maxTenantCount = maxTenantCount;
    }

    private static void ValidateTenant(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentNullException(nameof(tenant), "Tenant identifier must not be null or empty.");
        }
    }

    /// <summary>
    /// Resolves (or admits) the canonical entry for <paramref name="tenant"/>, enforcing
    /// <see cref="_maxTenantCount"/> atomically against the dictionary check-and-add. Only the
    /// fast dictionary check+add happens under <see cref="_admissionLock"/>; the entry's own
    /// construction is deferred inside <see cref="TenantContextEntry.LazyContext"/> and resolved
    /// outside the lock, so hold time stays negligible even though tenant construction itself may
    /// be slow.
    /// </summary>
    private TenantContextEntry GetOrCreateEntry(string tenant, bool constructAsync)
    {
        if (_contexts.TryGetValue(tenant, out var existing))
        {
            return existing;
        }

        if (_maxTenantCount.HasValue)
        {
            lock (_admissionLock)
            {
                if (_contexts.TryGetValue(tenant, out existing))
                {
                    return existing;
                }

                if (_contexts.Count >= _maxTenantCount.Value)
                {
                    throw new InvalidOperationException(
                        $"TenantContextRegistry has reached its maximum tenant count of {_maxTenantCount}. " +
                        "Call Invalidate() or InvalidateAll() to evict unused tenants before adding new ones.");
                }

                return _contexts.GetOrAdd(tenant, key => NewEntry(key, constructAsync));
            }
        }

        return _contexts.GetOrAdd(tenant, key => NewEntry(key, constructAsync));
    }

    // The shared construction never takes a caller's token: one caller cancelling its own wait
    // must not fail the construction other callers are waiting on (see ResolveEntryAsync).
    private TenantContextEntry NewEntry(string tenant, bool constructAsync) =>
        constructAsync
            ? new TenantContextEntry(() => CreateDatabaseContextAsync(tenant))
            : new TenantContextEntry(() => Task.FromResult(CreateDatabaseContext(tenant)));

    /// <summary>
    /// Resolves an entry's value, removing the entry on a faulted construction so the next
    /// caller starts a fresh attempt instead of repeatedly retrying (or observing) a stale entry.
    /// </summary>
    private IDatabaseContext ResolveEntry(string tenant, TenantContextEntry entry)
    {
        try
        {
            // Completed already unless the entry was admitted by an async caller whose
            // construction is still in flight; then this waits for it, as Lazy<T>.Value did.
            return entry.LazyContext.Value.GetAwaiter().GetResult();
        }
        catch
        {
            // TryRemove(KeyValuePair) checks both key and value before removing, making
            // concurrent fault scenarios safe: a second caller who already replaced this
            // entry with a fresh one is not affected by our removal attempt.
            _contexts.TryRemove(new KeyValuePair<string, TenantContextEntry>(tenant, entry));
            throw;
        }
    }

    /// <summary>
    /// Async <see cref="ResolveEntry"/>. Evicts the entry only when the shared construction itself
    /// failed, not when this caller's own token stopped its wait.
    /// </summary>
    private async Task<IDatabaseContext> ResolveEntryAsync(string tenant, TenantContextEntry entry,
        CancellationToken cancellationToken)
    {
        var task = entry.LazyContext.Value;
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                _contexts.TryRemove(new KeyValuePair<string, TenantContextEntry>(tenant, entry));
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public IDatabaseContext GetContext(string tenant)
    {
        ThrowIfDisposed();
        ValidateTenant(tenant);

        var entry = GetOrCreateEntry(tenant, constructAsync: false);
        return ResolveEntry(tenant, entry);
    }

    /// <inheritdoc/>
    public Task<IDatabaseContext> GetContextAsync(string tenant, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTenant(tenant);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = GetOrCreateEntry(tenant, constructAsync: true);
        return ResolveEntryAsync(tenant, entry, cancellationToken);
    }

    /// <inheritdoc/>
    public ITenantContextLease AcquireLease(string tenant)
    {
        ThrowIfDisposed();
        ValidateTenant(tenant);

        while (true)
        {
            var entry = GetOrCreateEntry(tenant, constructAsync: false);
            if (!entry.TryAddLease())
            {
                // Entry already committed to disposal — retry against a fresh/current entry.
                continue;
            }

            IDatabaseContext context;
            try
            {
                context = ResolveEntry(tenant, entry);
            }
            catch
            {
                entry.ReleaseLease(this, tenant);
                throw;
            }

            return new TenantContextLease(context, entry, this, tenant);
        }
    }

    /// <inheritdoc/>
    public async Task<ITenantContextLease> AcquireLeaseAsync(string tenant, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTenant(tenant);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = GetOrCreateEntry(tenant, constructAsync: true);
            if (!entry.TryAddLease())
            {
                // Entry already committed to disposal — retry against a fresh/current entry.
                continue;
            }

            IDatabaseContext context;
            try
            {
                context = await ResolveEntryAsync(tenant, entry, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                entry.ReleaseLease(this, tenant);
                throw;
            }

            return new TenantContextLease(context, entry, this, tenant);
        }
    }

    /// <summary>
    /// Queues actual disposal onto the thread pool rather than running it inline on the caller's
    /// thread. <see cref="TenantContextEntry.ReleaseLease"/>, <see cref="TenantContextEntry.MarkRemoved"/>,
    /// and transitively <see cref="Invalidate"/>/<see cref="ITenantContextLease.Dispose"/>, are all
    /// documented as never blocking the caller — but tearing down a <see cref="DatabaseContext"/>
    /// can itself block synchronously (its own pool-governor drain wait). Dispatching it here
    /// instead avoids that thread becoming a synchronous-blocking passenger competing with every
    /// other worker thread for the same limited pool.
    /// </summary>
    private void ScheduleDisposeEntry(string tenant, TenantContextEntry entry)
    {
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var (owner, capturedTenant, capturedEntry) = state;
            owner.DisposeEntry(capturedTenant, capturedEntry);
        }, (this, tenant, entry), preferLocal: false);
    }

    /// <summary>
    /// Actually tears down a tenant's context — always invoked via <see cref="ScheduleDisposeEntry"/>,
    /// never inline on a lease-releasing/invalidating caller's thread. If an asynchronous
    /// construction is still in flight, defers via a continuation instead of blocking.
    /// </summary>
    private void DisposeEntry(string tenant, TenantContextEntry entry)
    {
        var task = entry.LazyContext.Value;
        if (!task.IsCompleted)
        {
            task.ContinueWith(_ => DisposeEntry(tenant, entry), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }

        if (!task.IsCompletedSuccessfully)
        {
            return; // Faulted/canceled construction — nothing to dispose.
        }

        var context = task.Result;

        if (!entry.TryClaimDisposal())
        {
            return; // Already claimed by a racing registry shutdown — see TryClaimDisposal.
        }

        try
        {
            context.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing tenant context during invalidation for tenant '{Tenant}'.",
                tenant);
        }
        finally
        {
            RaiseContextRemoved(context);
        }
    }

    // ContextCreated runs application code. If it throws, the caller gets the exception and the
    // context nobody will receive is disposed (REV-030).
    private void RaiseContextCreated(IDatabaseContext context)
    {
        try
        {
            ContextCreated?.Invoke(context);
        }
        catch
        {
            DisposeRejectedContext(context);
            throw;
        }
    }

    private void DisposeRejectedContext(IDatabaseContext context)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing a tenant context after its ContextCreated handler threw.");
        }
    }

    // ContextRemoved runs application code, often on a thread-pool work item where an exception
    // would end the process (REV-031), so a failing handler is logged, never rethrown.
    private void RaiseContextRemoved(IDatabaseContext context)
    {
        try
        {
            ContextRemoved?.Invoke(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A ContextRemoved handler threw.");
        }
    }

    /// <inheritdoc/>
    public void Invalidate(string tenant)
    {
        if (_contexts.TryRemove(tenant, out var entry))
        {
            entry.MarkRemoved(this, tenant);
        }
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        foreach (var key in _contexts.Keys.ToArray())
        {
            Invalidate(key);
        }
    }

    private IDatabaseContext CreateDatabaseContext(string tenant)
    {
        var config = _resolver.GetDatabaseContextConfiguration(tenant);

        var factory = _serviceProvider.GetKeyedService<DbProviderFactory>(config.ProviderName)
                      ?? throw new InvalidOperationException($"No factory registered for '{config.ProviderName}'.");

        var context = _contextFactory.Create(config, factory, _loggerFactory);
        RaiseContextCreated(context);
        return context;
    }

    private async Task<IDatabaseContext> CreateDatabaseContextAsync(string tenant)
    {
        var config = _resolver.GetDatabaseContextConfiguration(tenant);

        var factory = _serviceProvider.GetKeyedService<DbProviderFactory>(config.ProviderName)
                      ?? throw new InvalidOperationException($"No factory registered for '{config.ProviderName}'.");

        var context = await _contextFactory.CreateAsync(config, factory, _loggerFactory, CancellationToken.None)
            .ConfigureAwait(false);
        RaiseContextCreated(context);
        return context;
    }

    /// <summary>
    /// Disposes every already-constructed context. An entry still under construction on another
    /// thread at this instant (<see cref="TenantContextEntry.LazyContext"/> not yet
    /// <see cref="Lazy{T}.IsValueCreated"/>) is handed to a background work item
    /// (<see cref="ScheduleBackgroundShutdownDisposal"/>) that blocks on its
    /// <see cref="Lazy{T}.Value"/> exactly like any other racing caller would, then disposes the
    /// result — so a context that finishes constructing after this method returns is never left
    /// unreachable/leaked, and this method itself never blocks on someone else's in-flight
    /// construction.
    /// </summary>
    protected override void DisposeManaged()
    {
        var entries = _contexts.Values.ToArray();
        _contexts.Clear();

        foreach (var entry in entries)
        {
            if (entry.LazyContext.IsValueCreated && entry.LazyContext.Value.IsCompleted)
            {
                var task = entry.LazyContext.Value;
                if (task.IsCompletedSuccessfully && entry.TryClaimDisposal())
                {
                    DisposeShutdownContextSync(task.Result);
                }
            }
            else
            {
                ScheduleBackgroundShutdownDisposal(entry);
            }
        }
    }

    /// <summary>
    /// Async counterpart of <see cref="DisposeManaged"/>. Unlike the sync path, an entry still
    /// under construction is awaited (without blocking a thread) and its context disposed before
    /// this method completes, so no tenant context outlives <c>DisposeAsync</c>.
    /// </summary>
    protected override async ValueTask DisposeManagedAsync()
    {
        var entries = _contexts.Values.ToArray();
        _contexts.Clear();

        foreach (var entry in entries)
        {
            if (entry.LazyContext.IsValueCreated && entry.LazyContext.Value.IsCompleted)
            {
                var task = entry.LazyContext.Value;
                if (task.IsCompletedSuccessfully && entry.TryClaimDisposal())
                {
                    await DisposeShutdownContextAsync(task.Result).ConfigureAwait(false);
                }
            }
            else
            {
                // DisposeManagedAsync is already asynchronous, so it awaits in-flight construction
                // and disposes inline instead of fire-and-forgetting it — preserving
                // IAsyncDisposable's "resources are released by the time DisposeAsync returns"
                // guarantee. It awaits the entry's completion signal rather than touching
                // LazyContext.Value, whose getter blocks synchronously while another thread is
                // mid-construction; no thread (pool or caller) is parked waiting.
                Task? started = null;
                if (!entry.ConstructionStarted)
                {
                    // Nobody has begun evaluating yet (the admitting caller is between
                    // GetOrCreateEntry and ResolveEntry). Start it on the pool so the signal is
                    // guaranteed to complete; if a caller starts it first, this simply observes
                    // that caller's result.
                    started = Task.Run(async () =>
                    {
                        try
                        {
                            await entry.LazyContext.Value.ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // The completion signal reports null; nobody else observes this failure.
                            LogShutdownConstructionFailure(ex);
                        }
                    });
                }

                var completedContext = await entry.Constructed.ConfigureAwait(false);
                if (started != null)
                {
                    // The signal completes before that task logs a failed construction; DisposeAsync
                    // returns only once the failure is logged (REV-043). The task never throws.
                    await started.ConfigureAwait(false);
                }

                if (completedContext != null && entry.TryClaimDisposal())
                {
                    await DisposeShutdownContextAsync(completedContext).ConfigureAwait(false);
                }
            }
        }
    }

    private void ScheduleBackgroundShutdownDisposal(TenantContextEntry entry)
    {
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var (owner, capturedEntry) = state;
            // Starts construction if nobody has (a synchronous factory then runs on this background
            // thread, never the caller's); an in-flight async construction is not blocked on.
            capturedEntry.LazyContext.Value.ContinueWith(static (task, state) =>
            {
                var (registry, shutdownEntry) = ((TenantContextRegistry, TenantContextEntry))state!;
                if (!task.IsCompletedSuccessfully)
                {
                    // Construction faulted: nothing to dispose, and nobody else observes this failure.
                    registry.LogShutdownConstructionFailure(task.Exception?.GetBaseException()
                                                            ?? new OperationCanceledException());
                    return;
                }

                if (!shutdownEntry.TryClaimDisposal())
                {
                    return; // Already claimed by a racing Invalidate — see TryClaimDisposal.
                }

                registry.DisposeShutdownContextSync(task.Result);
            }, (owner, capturedEntry), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }, (this, entry), preferLocal: false);
    }

    private void LogShutdownConstructionFailure(Exception ex)
    {
        _logger.LogWarning(ex, "A tenant context that was still being constructed during registry shutdown failed to construct.");
    }

    private void DisposeShutdownContextSync(IDatabaseContext context)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing tenant context during shutdown.");
        }

        // Outside the dispose's try: a failed dispose still removes the context (REV-032).
        RaiseContextRemoved(context);
    }

    private async ValueTask DisposeShutdownContextAsync(IDatabaseContext context)
    {
        try
        {
            if (context is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                context.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error asynchronously disposing tenant context during shutdown.");
        }

        // Outside the dispose's try: a failed dispose still removes the context (REV-032).
        RaiseContextRemoved(context);
    }
}
