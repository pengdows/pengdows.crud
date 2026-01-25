#region

#endregion

namespace pengdows.crud;

/// <summary>
/// Legacy name retained for compatibility while `<see cref="ITableGateway{TEntity,TRowID}"/>` becomes primary.
/// </summary>
[Obsolete("Renamed to ITableGateway<TEntity, TRowID> in v2.0")]
public interface IEntityHelper<TEntity, TRowID> :
    ITableGateway<TEntity, TRowID>
    where TEntity : class, new()
{
}