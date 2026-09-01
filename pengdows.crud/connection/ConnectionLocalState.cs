using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;

namespace pengdows.crud.connection;

/// <summary>
/// Per-connection state for tracking prepare behavior and caching
/// </summary>
public sealed class ConnectionLocalState : IConnectionLocalState
{
    /// <summary>
    /// Whether prepare has been disabled for this connection due to failures
    /// </summary>
    public bool PrepareDisabled { get; set; }

    /// <summary>
    /// Tracks whether session settings have already been applied for this connection
    /// </summary>
    public bool SessionSettingsApplied { get; set; }

    private ConcurrentDictionary<string, byte>? _prepared;
    private ConcurrentQueue<string>? _order;
    private const int _maxPrepared = 32;

    /// <summary>
    /// Computes a hash of the command's SQL text and parameter types for shape caching
    /// </summary>
    public static string ComputeShapeHash(DbCommand cmd)
    {
        return cmd.CommandText;
    }

    /// <summary>
    /// Checks if the command shape matches the last prepared shape
    /// </summary>
    public bool IsAlreadyPreparedForShape(string shapeHash)
    {
        var prepared = Volatile.Read(ref _prepared);
        return prepared != null && prepared.ContainsKey(shapeHash);
    }

    /// <inheritdoc/>
    public void DisablePrepare() => PrepareDisabled = true;

    /// <inheritdoc/>
    public void MarkSessionSettingsApplied() => SessionSettingsApplied = true;

    /// <summary>
    /// Marks this shape as prepared
    /// </summary>
    public (bool Added, int Evicted) MarkShapePrepared(string shapeHash)
    {
        var prepared = GetPreparedCache();
        var order = GetPreparedOrder();
        if (prepared.TryAdd(shapeHash, 0))
        {
            order.Enqueue(shapeHash);
            var evicted = 0;
            while (prepared.Count > _maxPrepared && order.TryDequeue(out var old))
            {
                if (prepared.TryRemove(old, out _))
                {
                    evicted++;
                }
            }

            return (true, evicted);
        }

        return (false, 0);
    }

    /// <summary>
    /// Resets prepare state (e.g., when connection is recycled)
    /// </summary>
    public void Reset()
    {
        var order = Volatile.Read(ref _order);
        while (order != null && order.TryDequeue(out _))
        {
        }

        var prepared = Volatile.Read(ref _prepared);
        prepared?.Clear();
        // Don't reset PrepareDisabled - that should persist for the physical connection
        SessionSettingsApplied = false;
    }

    private ConcurrentDictionary<string, byte> GetPreparedCache()
    {
        var prepared = Volatile.Read(ref _prepared);
        if (prepared != null)
        {
            return prepared;
        }

        prepared = new ConcurrentDictionary<string, byte>();
        var existing = Interlocked.CompareExchange(ref _prepared, prepared, null);
        return existing ?? prepared;
    }

    private ConcurrentQueue<string> GetPreparedOrder()
    {
        var order = Volatile.Read(ref _order);
        if (order != null)
        {
            return order;
        }

        order = new ConcurrentQueue<string>();
        var existing = Interlocked.CompareExchange(ref _order, order, null);
        return existing ?? order;
    }
}