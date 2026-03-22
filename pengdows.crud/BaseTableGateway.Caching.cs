// =============================================================================
// FILE: BaseTableGateway.Caching.cs
// PURPOSE: Cache management and cleanup for shared gateway caches.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: Cache constants and cleanup methods.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    /// <summary>
    /// Clears all internal caches. Useful for testing or after schema changes.
    /// </summary>
    public void ClearCaches()
    {
        _readerPlans.Clear();
        _columnListCache.Clear();
        foreach (var cache in _queryCache.Values) cache.Clear();
        foreach (var cache in _whereParameterNames.Values) cache.Clear();
    }
}
