using System.Collections.Concurrent;
using pengdows.crud.connection;

namespace pengdows.crud.Tests;

internal sealed class TestConnectionLocalState : IConnectionLocalState
{
    public bool PrepareDisabled { get; private set; }
    public bool SessionSettingsApplied { get; private set; }

    private readonly ConcurrentDictionary<string, byte> _prepared = new();
    private readonly ConcurrentQueue<string> _order = new();
    private const int MaxPrepared = 32;

    public void DisablePrepare() => PrepareDisabled = true;

    public void MarkSessionSettingsApplied() => SessionSettingsApplied = true;

    public bool IsAlreadyPreparedForShape(string shapeHash) => _prepared.ContainsKey(shapeHash);

    public (bool Added, int Evicted) MarkShapePrepared(string shapeHash)
    {
        if (!_prepared.TryAdd(shapeHash, 0))
        {
            return (false, 0);
        }

        _order.Enqueue(shapeHash);
        var evicted = 0;
        while (_prepared.Count > MaxPrepared && _order.TryDequeue(out var old))
        {
            if (_prepared.TryRemove(old, out _))
            {
                evicted++;
            }
        }

        return (true, evicted);
    }

    public void Reset()
    {
        while (_order.TryDequeue(out _))
        {
        }

        _prepared.Clear();
        SessionSettingsApplied = false;
    }
}
