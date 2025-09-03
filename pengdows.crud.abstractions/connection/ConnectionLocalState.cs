using System.Text;

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
    
    /// <summary>
    /// Cache of the last prepared statement shape to avoid re-preparing identical shapes
    /// </summary>
    private string? _lastShapeHash;
    private bool _isPreparedForShape;

    /// <summary>
    /// Computes a hash of the command's SQL text and parameter types for shape caching
    /// </summary>
    public static string ComputeShapeHash(System.Data.Common.DbCommand cmd)
    {
        var sb = new StringBuilder(cmd.CommandText.Length + cmd.Parameters.Count * 6);
        sb.Append(cmd.CommandText);
        
        foreach (System.Data.Common.DbParameter p in cmd.Parameters)
        {
            sb.Append('|').Append((int)p.DbType).Append(':').Append(p.Size);
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Checks if the command shape matches the last prepared shape
    /// </summary>
    public bool IsAlreadyPreparedForShape(string shapeHash)
    {
        return _isPreparedForShape && shapeHash == _lastShapeHash;
    }

    /// <summary>
    /// Marks this shape as prepared
    /// </summary>
    public void MarkShapePrepared(string shapeHash)
    {
        _lastShapeHash = shapeHash;
        _isPreparedForShape = true;
    }

    /// <summary>
    /// Resets prepare state (e.g., when connection is recycled)
    /// </summary>
    public void Reset()
    {
        _lastShapeHash = null;
        _isPreparedForShape = false;
        // Don't reset PrepareDisabled - that should persist for the physical connection
    }
}