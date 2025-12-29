using System.Collections.Concurrent;
using System.Data.Common;

namespace pengdows.crud.connection;

/// <summary>
/// Per-connection state for tracking prepare behavior and caching
/// </summary>
public sealed class ConnectionLocalState
{
    /// <summary>
    /// Whether prepare has been disabled for this connection due to failures
    /// </summary>
    public bool PrepareDisabled { get; set; }
    
    private readonly ConcurrentDictionary<string, byte> _prepared = new();
    private readonly ConcurrentQueue<string> _order = new();
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
        return _prepared.ContainsKey(shapeHash);
    }

    /// <summary>
    /// Marks this shape as prepared
    /// </summary>
    public bool MarkShapePrepared(string shapeHash, out int evicted)
    {
        evicted = 0;
        if (_prepared.TryAdd(shapeHash, 0))
        {
            _order.Enqueue(shapeHash);
            while (_prepared.Count > _maxPrepared && _order.TryDequeue(out var old))
            {
                if (_prepared.TryRemove(old, out _))
                {
                    evicted++;
                }
            }

            return true;
        }

        evicted = 0;
        return false;
    }

    /// <summary>
    /// Resets prepare state (e.g., when connection is recycled)
    /// </summary>
    public void Reset()
    {
        while (_order.TryDequeue(out _))
        {
        }

        _prepared.Clear();
        // Don't reset PrepareDisabled - that should persist for the physical connection
    }
}
