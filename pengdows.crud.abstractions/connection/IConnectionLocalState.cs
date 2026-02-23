using System.Data.Common;

namespace pengdows.crud.connection;

/// <summary>
/// Per-connection state for tracking prepare behavior and caching.
/// </summary>
public interface IConnectionLocalState
{
    /// <summary>
    /// Whether prepare has been disabled for this connection due to failures.
    /// </summary>
    bool PrepareDisabled { get; set; }

    /// <summary>
    /// Tracks whether session settings have already been applied for this connection.
    /// </summary>
    bool SessionSettingsApplied { get; set; }

    /// <summary>
    /// Checks if the command shape matches a previously prepared shape.
    /// </summary>
    bool IsAlreadyPreparedForShape(string shapeHash);

    /// <summary>
    /// Marks this shape as prepared and returns true if newly added.
    /// Evicts oldest shapes when the cache exceeds its limit.
    /// </summary>
    /// <param name="shapeHash">The shape hash to mark as prepared.</param>
    /// <param name="evicted">Number of shapes evicted from the cache.</param>
    /// <returns>True if newly added; false if already present.</returns>
    bool MarkShapePrepared(string shapeHash, out int evicted);

    /// <summary>
    /// Resets prepare state (e.g., when connection is recycled).
    /// </summary>
    void Reset();
}