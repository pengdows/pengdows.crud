// =============================================================================
// FILE: TenantContextRegistry.cs
// PURPOSE: Registry managing DatabaseContext instances per tenant.
//
// AI SUMMARY:
// - Implements ITenantContextRegistry for tenant context lifecycle management.
// - Thread-safe: ConcurrentDictionary<string, TenantContextEntry>. Each entry wraps a
//   Lazy<Task<IDatabaseContext>> (single-flight construction, sync AND async callers share it —
//   see TenantContextEntry below) plus a CAS-based lease refcount that lets AcquireLease/
//   AcquireLeaseAsync defer Invalidate's disposal until every outstanding lease releases.
// - GetContext/GetContextAsync: bare-reference reads, same historical residual race against a
//   concurrent Invalidate (no lease taken) — unchanged contract, now sharing the same cache/dedup
//   mechanism as the leased API instead of a separate one.
// - AcquireLease/AcquireLeaseAsync: reference-counted lease API (closes CORE-010) — a leased
//   context is guaranteed not to be disposed by Invalidate/InvalidateAll until the lease itself is
//   disposed.
// - Invalidate(tenant): removes the tenant's entry; disposal is immediate if idle (no leases),
//   deferred to the last lease's release otherwise. Never blocks the calling thread.
// - InvalidateAll(): evicts all cached contexts; next GetContext/AcquireLease recreates each.
// - Optional MaxTenantCount cap: throws when adding a new tenant would exceed the limit. Entries
//   mid-drain (invalidated, waiting on outstanding leases) no longer count against the cap.
// - Extends SafeAsyncDisposableBase for proper cleanup.
// - DisposeManaged/Async: disposes every already-completed context unconditionally regardless of
//   outstanding leases — shutdown is terminal by design (leases protect against Invalidate during
//   live operation, not against total application shutdown).
// =============================================================================

using System.Collections.Concurrent;
using System.Data.Common;
using System;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
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
///   <item>Update the tenant's configuration via <c>ITenantConnectionResolver.Register</c>.</item>
///   <item>Call <see cref="Invalidate"/> (or <see cref="InvalidateAll"/>) to evict the stale context.</item>
///   <item>The next <see cref="GetContext"/> call creates a fresh context using the new configuration.</item>
/// </list>
/// <para>
/// <b>Disposal:</b> The registry owns every context it creates. After this registry is
/// disposed, <see cref="GetContext"/> throws <see cref="ObjectDisposedException"/>, and
/// disposal itself disposes every already-created tenant context (the same "registry owns and
/// disposes what it creates" contract <see cref="Invalidate"/>/<see cref="InvalidateAll"/> use
/// for a single tenant, just applied to all of them at once). A context handed back by
/// <see cref="GetContext"/> before registry disposal is not safe to keep using afterward —
/// callers that need a context to outlive the registry must not rely on this registry for that
/// context's lifetime.
/// </para>
/// <para>
/// <b>Cardinality:</b> The optional <c>maxTenantCount</c> constructor parameter enforces an upper
/// bound on distinct tenants. Unbounded registries in long-lived apps with many tenants can cause
/// connection-pool explosion; call <see cref="InvalidateAll"/> or use the cap accordingly.
/// </para>
/// <para>
/// <b>Live rotation:</b> <see cref="Invalidate"/>/<see cref="InvalidateAll"/> combined with
/// <see cref="AcquireLease"/>/<see cref="AcquireLeaseAsync"/> make live tenant ejection/rotation a
/// safe, supported pattern — a leased context survives a concurrent rotation until the lease is
/// released. <see cref="GetContext"/>/<see cref="GetContextAsync"/> remain the simpler, bare-
/// reference option for the common "resolve, then immediately use" case.
/// </para>
/// </remarks>
public class TenantContextRegistry : SafeAsyncDisposableBase, ITenantContextRegistry
{
    /// <summary>
    /// A single tenant's cached construction + lease state. <see cref="LazyContext"/> gives every
    /// caller (sync or async) single-flight construction dedup via
    /// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> — the factory delegate only has
    /// to *start* the async work (return a <see cref="Task{TResult}"/>), which is non-blocking,
    /// unlike wrapping a <c>Lazy&lt;IDatabaseContext&gt;</c> around real connection I/O.
    /// </summary>
    /// <remarks>
    /// <see cref="_leaseCount"/> doubles as the exactly-once disposal guard: <see cref="Dead"/> is
    /// a reserved sentinel meaning "disposal already committed." <see cref="TryAddLease"/> is a CAS
    /// loop rather than a plain increment specifically because a plain increment cannot detect
    /// "someone already committed to disposing this" — it would let a lease resurrect a reference
    /// to an already-disposed context. Whichever of <see cref="ReleaseLease"/> or
    /// <see cref="MarkRemoved"/> wins the CAS transition from 0 to <see cref="Dead"/> is the sole
    /// owner of calling <see cref="TenantContextRegistry.DisposeEntry"/> — no separate guard flag
    /// needed.
    /// </remarks>
    internal sealed class TenantContextEntry
    {
        private const int Dead = int.MinValue;

        private int _leaseCount;
        private int _removed;

        public Lazy<Task<IDatabaseContext>> LazyContext { get; }

        public TenantContextEntry(Func<Task<IDatabaseContext>> factory)
        {
            LazyContext = new Lazy<Task<IDatabaseContext>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
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
        /// entry has since been removed via <see cref="MarkRemoved"/>, disposes it.
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
        /// outstanding leases) at this instant, disposes immediately — matching the registry's
        /// pre-leasing behavior exactly for the common zero-lease case. Otherwise defers to
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
    /// <see cref="GetContext"/> throws <see cref="InvalidOperationException"/> for new tenants.
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
    /// <see cref="_maxTenantCount"/> atomically against the dictionary check-and-add, exactly as
    /// before leasing existed. The entry's own construction is deferred (inside
    /// <see cref="TenantContextEntry.LazyContext"/>) — this method never blocks on real I/O.
    /// </summary>
    private TenantContextEntry GetOrCreateEntry(string tenant, CancellationToken cancellationToken)
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

                return _contexts.GetOrAdd(tenant,
                    key => new TenantContextEntry(() => CreateDatabaseContextAsync(key, cancellationToken)));
            }
        }

        return _contexts.GetOrAdd(tenant,
            key => new TenantContextEntry(() => CreateDatabaseContextAsync(key, cancellationToken)));
    }

    private IDatabaseContext ResolveEntrySync(string tenant, TenantContextEntry entry)
    {
        var task = entry.LazyContext.Value;
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch
        {
            // Remove the faulted entry so the next call gets a fresh attempt. TryRemove(KeyValuePair)
            // checks both key and value before removing, making concurrent fault scenarios safe —
            // see the historical rationale in ResolveEntryAsync's identical pattern.
            _contexts.TryRemove(new KeyValuePair<string, TenantContextEntry>(tenant, entry));
            throw;
        }
    }

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
            // Only evict on a genuine construction failure — not merely because THIS caller's own
            // token stopped waiting while the shared construction is still in flight for others.
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
        ValidateTenant(tenant);

        // Looped rather than a single pass: a racing Invalidate() (or registry disposal) can
        // evict our freshly-installed entry before we can confirm it was ever published — see the
        // re-check below. ThrowIfDisposed() is re-checked on every iteration (not just once up
        // front) so a retry forced by a disposal race fails closed with ObjectDisposedException
        // instead of looping forever against a disposed registry.
        while (true)
        {
            ThrowIfDisposed();

            // Fast path: already-cached tenant — no re-check needed. This entry was observed
            // registered at the moment of lookup; a subsequent concurrent Invalidate is not a
            // leak (disposal ownership belongs entirely to the entry's own Mark/ReleaseLease
            // logic now), just an ordinary point-in-time race — the same one GetContext has
            // always accepted for callers who don't need AcquireLease's stronger guarantee.
            if (_contexts.TryGetValue(tenant, out var existingEntry))
            {
                return ResolveEntrySync(tenant, existingEntry);
            }

            var entry = GetOrCreateEntry(tenant, CancellationToken.None);
            var context = ResolveEntrySync(tenant, entry);

            if (_contexts.TryGetValue(tenant, out var currentEntry) && ReferenceEquals(currentEntry, entry))
            {
                return context;
            }

            // A racing Invalidate()/registry disposal evicted our just-installed entry before we
            // could observe it as published. No lease was taken, so that entry's own
            // MarkRemoved/DisposeEntry already owns disposing `context` (immediately, or via a
            // continuation if construction hadn't finished yet) — just retry.
        }
    }

    /// <inheritdoc/>
    public async Task<IDatabaseContext> GetContextAsync(string tenant, CancellationToken cancellationToken = default)
    {
        ValidateTenant(tenant);

        while (true)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (_contexts.TryGetValue(tenant, out var existingEntry))
            {
                return await ResolveEntryAsync(tenant, existingEntry, cancellationToken).ConfigureAwait(false);
            }

            var entry = GetOrCreateEntry(tenant, cancellationToken);
            var context = await ResolveEntryAsync(tenant, entry, cancellationToken).ConfigureAwait(false);

            if (_contexts.TryGetValue(tenant, out var currentEntry) && ReferenceEquals(currentEntry, entry))
            {
                return context;
            }

            // Same race as GetContext's identical check — retry, disposal already owned elsewhere.
        }
    }

    /// <inheritdoc/>
    public ITenantContextLease AcquireLease(string tenant)
    {
        ValidateTenant(tenant);

        while (true)
        {
            ThrowIfDisposed();

            var entry = GetOrCreateEntry(tenant, CancellationToken.None);
            if (!entry.TryAddLease())
            {
                // Entry already committed to disposal — retry against a fresh/current entry.
                continue;
            }

            IDatabaseContext context;
            try
            {
                context = ResolveEntrySync(tenant, entry);
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
        ValidateTenant(tenant);

        while (true)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var entry = GetOrCreateEntry(tenant, cancellationToken);
            if (!entry.TryAddLease())
            {
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
    /// Queues <see cref="DisposeEntry"/> onto the thread pool rather than running it inline on the
    /// caller's thread. <see cref="TenantContextEntry.ReleaseLease"/>, <see cref="TenantContextEntry.MarkRemoved"/>,
    /// and transitively <see cref="Invalidate"/>/<see cref="ITenantContextLease.Dispose"/>, are all
    /// documented as never blocking the caller — but tearing down a <see cref="DatabaseContext"/>
    /// can itself block synchronously (its own pool-governor drain wait). Confirmed empirically
    /// this matters in practice, not just in theory: running disposal inline on the releasing/
    /// invalidating caller's thread measurably contributes to thread-pool contention once many
    /// concurrent lease/release/invalidate cycles are in flight across the process (the exact
    /// scenario <see cref="AcquireLease"/>/<see cref="AcquireLeaseAsync"/> exist to support) —
    /// dispatching it here instead avoids that thread becoming a synchronous-blocking passenger
    /// competing with every other worker thread for the same limited pool.
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
    /// never inline on a lease-releasing/invalidating caller's thread. If construction is still in
    /// flight, defers via a continuation instead of blocking.
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
            ContextRemoved?.Invoke(context);
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

    private DbProviderFactory ResolveProviderFactory(IDatabaseContextConfiguration config)
    {
        // CORE-006: DbProviderLoader registers its keyed DI service under the DatabaseProviders
        // configuration section KEY, not the ProviderName field inside that section (see
        // DbProviderLoader.LoadAndRegisterProviders) — so a tenant's ProviderName must equal
        // that section key, not necessarily the ADO.NET invariant name its own name suggests.
        // A caller who (reasonably) sets it to the invariant name gets a confusing bare failure
        // otherwise; spell out the actual requirement here instead.
        return _serviceProvider.GetKeyedService<DbProviderFactory>(config.ProviderName)
               ?? throw new InvalidOperationException(
                   $"No DbProviderFactory registered for the key '{config.ProviderName}'. " +
                   "The tenant configuration's ProviderName must match a DatabaseProviders " +
                   "configuration section key (e.g. \"DatabaseProviders:" +
                   $"{config.ProviderName}\": {{ ... }}), not necessarily the ADO.NET " +
                   "provider invariant name configured inside that section.");
    }

    // CORE-011: invoke every subscriber individually rather than a single multicast-delegate
    // Invoke() call, so one faulting subscriber does not prevent later, well-behaved subscribers
    // from ever being notified. If any subscriber faults, `context` must not be published to the
    // caller or cached for reuse (the entry wrapping this call gets evicted by the caller's own
    // fault handling) — dispose it here, since nobody else can ever reach it, then propagate the
    // first subscriber's exception.
    private void NotifyContextCreatedOrDisposeOnFailure(string tenant, IDatabaseContext context)
    {
        var handler = ContextCreated;
        if (handler == null)
        {
            return;
        }

        Exception? first = null;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<IDatabaseContext>)subscriber).Invoke(context);
            }
            catch (Exception ex)
            {
                first ??= ex;
                _logger.LogWarning(ex,
                    "A ContextCreated subscriber threw while notifying tenant '{Tenant}'.", tenant);
            }
        }

        if (first != null)
        {
            try
            {
                context.Dispose();
            }
            catch (Exception disposeEx)
            {
                _logger.LogWarning(disposeEx,
                    "Error disposing a context after a ContextCreated subscriber failure for tenant '{Tenant}'.",
                    tenant);
            }

            ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    private async Task<IDatabaseContext> CreateDatabaseContextAsync(string tenant, CancellationToken cancellationToken)
    {
        var config = _resolver.GetDatabaseContextConfiguration(tenant);
        var factory = ResolveProviderFactory(config);
        var context = await _contextFactory.CreateAsync(config, factory, _loggerFactory, cancellationToken)
            .ConfigureAwait(false);
        NotifyContextCreatedOrDisposeOnFailure(tenant, context);
        return context;
    }

    protected override void DisposeManaged()
    {
        foreach (var entry in _contexts.Values)
        {
            if (!entry.LazyContext.IsValueCreated)
            {
                continue;
            }

            var task = entry.LazyContext.Value;
            if (!task.IsCompletedSuccessfully)
            {
                continue;
            }

            try
            {
                var context = task.Result;
                context.Dispose();
                ContextRemoved?.Invoke(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing tenant context during shutdown.");
            }
        }

        _contexts.Clear();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        foreach (var entry in _contexts.Values)
        {
            if (!entry.LazyContext.IsValueCreated)
            {
                continue;
            }

            var task = entry.LazyContext.Value;
            if (!task.IsCompletedSuccessfully)
            {
                continue;
            }

            try
            {
                var context = task.Result;
                if (context is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    context.Dispose();
                }

                ContextRemoved?.Invoke(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error asynchronously disposing tenant context during shutdown.");
            }
        }

        _contexts.Clear();
    }
}
