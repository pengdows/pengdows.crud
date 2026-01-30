// =============================================================================
// FILE: TableGateway.Caching.cs
// PURPOSE: Cache management and cleanup for TableGateway internal caches.
//
// AI SUMMARY:
// - MaxCacheSize constant limits bounded caches to prevent memory growth.
// - ClearCaches() method clears all internal caches:
//   * _readerPlans - Compiled DataReader-to-entity mapping plans
//   * _columnListCache - Column lists for SQL generation
//   * _queryCache - Pre-built SQL query strings
//   * _whereParameterNames - Parameter name arrays for WHERE clauses
// - Caches are BoundedCache instances with LRU eviction.
// - Call ClearCaches() if entity schema changes at runtime (rare).
// - Thread-safe: bounded caches handle concurrent access.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: Cache constants and cleanup methods.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    /// <summary>
    /// Maximum number of entries in bounded caches before LRU eviction.
    /// </summary>
    private const int MaxCacheSize = 100;

    /// <summary>
    /// Clears all internal caches. Useful for testing or after schema changes.
    /// </summary>
    /// <remarks>
    /// This method clears:
    /// <list type="bullet">
    /// <item><description>Reader plans - Compiled row-to-entity mappings</description></item>
    /// <item><description>Column list cache - Columns by operation type</description></item>
    /// <item><description>Query cache - Pre-built SQL strings</description></item>
    /// <item><description>Parameter name cache - WHERE clause parameters</description></item>
    /// </list>
    /// </remarks>
    public void ClearCaches()
    {
        _readerPlans.Clear();
        _columnListCache.Clear();
        _queryCache.Clear();
        _whereParameterNames.Clear();
    }
}