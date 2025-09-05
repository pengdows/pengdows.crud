namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    private const int MaxCacheSize = 100;

    public void ClearCaches()
    {
        _readerConverters.Clear();
        _readerPlans.Clear();
        _columnListCache.Clear();
        _queryCache.Clear();
        _whereParameterNames.Clear();
    }
}
