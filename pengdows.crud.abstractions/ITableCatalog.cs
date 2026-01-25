namespace pengdows.crud;

/// <summary>
/// Catalog of table-level CRUD helpers and SQL builders for the given entity.
/// </summary>
/// <remarks>
/// This is the new primary contract that used to be exposed as <see cref="IEntityHelper{TEntity,TRowID}"/>.
/// <c>IEntityHelper</c> now just inherits this interface as a compatibility shell.
/// </remarks>
public interface ITableCatalog<TEntity, TRowID> : ITableGateway<TEntity, TRowID>
    where TEntity : class, new()
{
}
