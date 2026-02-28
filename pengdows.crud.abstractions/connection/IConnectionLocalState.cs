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
    bool PrepareDisabled { get; }

    /// <summary>
    /// Tracks whether session settings have already been applied for this connection.
    /// </summary>
    bool SessionSettingsApplied { get; }

    /// <summary>
    /// Disables prepare for this connection permanently (called when a prepare error occurs).
    /// </summary>
    void DisablePrepare();

    /// <summary>
    /// Marks that session settings have been successfully applied for this connection.
    /// </summary>
    void MarkSessionSettingsApplied();

    /// <summary>
    /// Checks if the command shape matches a previously prepared shape.
    /// </summary>
    bool IsAlreadyPreparedForShape(string shapeHash);

    /// <summary>
    /// Marks this shape as prepared and returns whether it was newly added and how many
    /// shapes were evicted to make room.
    /// </summary>
    /// <param name="shapeHash">The shape hash to mark as prepared.</param>
    /// <returns>
    /// <c>Added</c> is true if the shape was newly added; false if already present.
    /// <c>Evicted</c> is the number of oldest shapes evicted from the cache to enforce the size limit.
    /// </returns>
    (bool Added, int Evicted) MarkShapePrepared(string shapeHash);

    /// <summary>
    /// Resets prepare state (e.g., when connection is recycled).
    /// </summary>
    void Reset();
}