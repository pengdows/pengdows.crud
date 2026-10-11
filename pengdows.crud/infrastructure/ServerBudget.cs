// =============================================================================
// FILE: ServerBudget.cs
// PURPOSE: One connection budget per database server, shared by every governor that points at it (POOL-003).
//
// AI SUMMARY:
// - ServerBudgetRegistry is process-wide, keyed by ServerEndpoint (host[\instance]:port). A server's connection limit
//   belongs to the server, not to a database or a context, so contexts on different databases of one server share one
//   ServerBudget and a read replica (another endpoint) has its own.
// - A context registers what it probed (usable limit = server limit minus headroom) and the maximum of each governor
//   it creates. The budget keeps the smallest usable limit and the sum of registered maxima.
// - It ENGAGES (creates a gate and starts costing governors an atomic) only when the registered maxima together exceed
//   the limit; before that, governors pay one volatile read. Engagement latches: leaving registrants never disengage.
// - Three counts are different things: a local governor lease != a server permit != a provider-opened connection.
//   This type counts the middle one. Idle connections held by a provider pool are not counted (the remaining physical
//   gap, see docs/FUTURE_WORK.md POOL-003).
// - Leases already in flight when the budget engages hold no permit; the budget adopts them (each governor reports how
//   many) so a sentinel taken at start-up counts from then on, and returns of those leases give the adopted permits back.
// =============================================================================

namespace pengdows.crud.infrastructure;

internal sealed class ServerBudget
{
    private readonly object _sync = new();
    private readonly List<PoolGovernor> _governors = new();
    private int _limit = int.MaxValue;
    private int _sumMaximum;
    private int _registrants;
    private PoolGovernorConcurrencyGate? _gate;
    private volatile bool _engaged;

    /// <summary>The smallest usable limit any registrant probed. It never rises while the budget exists.</summary>
    internal int Limit => Volatile.Read(ref _limit);

    /// <summary>True once the registered governor maxima together exceed <see cref="Limit"/>; latched.</summary>
    internal bool Engaged => _engaged;

    internal int Registrants => Volatile.Read(ref _registrants);

    /// <summary>Server permits currently held (including adopted in-flight leases); 0 while disengaged.</summary>
    internal int InUse => Volatile.Read(ref _gate)?.ActiveCount ?? 0;

    internal void Add(int usableLimit, int configuredMaximum)
    {
        lock (_sync)
        {
            _registrants++;
            _sumMaximum += configuredMaximum;
            if (usableLimit < _limit)
            {
                _limit = usableLimit;
                _gate?.SetLimit(Math.Min(_limit, _gate.ConfiguredMaximum));
            }

            if (!_engaged && _sumMaximum > _limit)
            {
                Engage();
            }
        }
    }

    internal void Remove(int configuredMaximum)
    {
        lock (_sync)
        {
            _registrants--;
            _sumMaximum -= configuredMaximum;
        }
    }

    internal void Attach(PoolGovernor governor)
    {
        lock (_sync)
        {
            _governors.Add(governor);
        }
    }

    internal void Detach(PoolGovernor governor)
    {
        lock (_sync)
        {
            _governors.Remove(governor);
        }
    }

    // Under _sync. The gate exists before the flag is raised, so a governor that sees Engaged also sees a gate.
    private void Engage()
    {
        // A gate of one is a different mechanism (a single slot); keep at least two and cap the effective limit instead.
        var gate = new PoolGovernorConcurrencyGate(Math.Max(_limit, 2));
        if (_limit < 2)
        {
            gate.SetLimit(_limit);
        }

        Volatile.Write(ref _gate, gate);

        var adopted = 0;
        foreach (var governor in _governors)
        {
            adopted += governor.AdoptInFlight();
        }

        gate.AdoptActive(adopted);
        _engaged = true;
    }

    internal bool TryAcquire() => _gate!.TryAcquire(out _);

    /// <summary>Blocks for a permit; false when <paramref name="timeout"/> passes first.</summary>
    internal bool Acquire(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            _gate!.Acquire(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Awaits a permit; false when <paramref name="timeout"/> passes first.</summary>
    internal async ValueTask<bool> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _gate!.AcquireAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    internal void Release() => _gate!.Release(0);
}

/// <summary>One governor's claim on a <see cref="ServerBudget"/>. Disposing it is idempotent and leaves the budget.</summary>
internal sealed class ServerBudgetRegistration : IDisposable
{
    private readonly string _key;
    private readonly int _configuredMaximum;
    private int _disposed;

    internal ServerBudgetRegistration(string key, ServerBudget budget, int configuredMaximum)
    {
        _key = key;
        Budget = budget;
        _configuredMaximum = configuredMaximum;
    }

    internal ServerBudget Budget { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            ServerBudgetRegistry.Unregister(_key, Budget, _configuredMaximum);
        }
    }
}

internal static class ServerBudgetRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ServerBudget> Budgets = new(StringComparer.OrdinalIgnoreCase);

    internal static ServerBudgetRegistration Register(string endpointKey, int usableLimit, int configuredMaximum)
    {
        lock (Sync)
        {
            if (!Budgets.TryGetValue(endpointKey, out var budget))
            {
                budget = new ServerBudget();
                Budgets[endpointKey] = budget;
            }

            budget.Add(Math.Max(1, usableLimit), configuredMaximum);
            return new ServerBudgetRegistration(endpointKey, budget, configuredMaximum);
        }
    }

    internal static bool TryGet(string endpointKey, out ServerBudget budget)
    {
        lock (Sync)
        {
            return Budgets.TryGetValue(endpointKey, out budget!);
        }
    }

    internal static void Unregister(string endpointKey, ServerBudget budget, int configuredMaximum)
    {
        lock (Sync)
        {
            budget.Remove(configuredMaximum);
            if (budget.Registrants == 0 && Budgets.TryGetValue(endpointKey, out var current) &&
                ReferenceEquals(current, budget))
            {
                Budgets.Remove(endpointKey);
            }
        }
    }
}
