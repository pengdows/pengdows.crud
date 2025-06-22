#region

using System.Data.Common;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Provides SQL generation, mapping, and binding logic for a specific entity type.
/// Used for CRUD generation, parameter naming, and object materialization.
/// </summary>
public interface IEntityHelper<TEntity, TRowID> where TEntity : class, new()
{
    /// <summary>
    /// Fully qualified, quoted table name used by this entity.
    /// </summary>
    string WrappedTableName { get; }

    /// <summary>
    /// Determines what happens when enum parsing fails.
    /// </summary>
    EnumParseFailureMode EnumParseBehavior { get; set; }

    /// <summary>
    /// Builds a SQL INSERT for the given object.
    /// </summary>
    ISqlContainer BuildCreate(TEntity objectToCreate, IDatabaseContext? context = null);

    /// <summary>
    /// Returns a SELECT clause with no WHERE clause, aliased.
    /// </summary>
    ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null);

    /// <summary>
    /// Builds a SQL SELECT for a list of row IDs.
    /// </summary>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds = null, string alias = "a",
        IDatabaseContext? context = null);

    /// <summary>
    /// Builds a SQL SELECT for a list of object identities.
    /// </summary>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects = null, string alias = "a",
        IDatabaseContext? context = null);

    /// <summary>
    /// Overload for retrieving by ID without alias.
    /// </summary>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds = null, IDatabaseContext? context = null);

    /// <summary>
    /// Overload for retrieving by objects without alias.
    /// </summary>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects = null, IDatabaseContext? context = null);

    /// <summary>
    /// Builds an UPDATE statement asynchronously.
    /// </summary>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null);

    /// <summary>
    /// Builds an UPDATE statement, optionally reloading the original.
    /// </summary>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null);

    /// <summary>
    /// Builds a DELETE by primary key.
    /// </summary>
    ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null);

    /// <summary>
    /// Loads a single object from the database using primary key values.
    /// </summary>
    Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null);

    /// <summary>
    /// Loads a single object using a custom SQL container.
    /// </summary>
    Task<TEntity?> LoadSingleAsync(ISqlContainer sc);

    /// <summary>
    /// Loads a list of objects using the provided SQL container.
    /// </summary>
    Task<List<TEntity>> LoadListAsync(ISqlContainer sc);

    /// <summary>
    /// Generates a formatted parameter name based on the provided DbParameter.
    /// </summary>
    string MakeParameterName(DbParameter p);

    /// <summary>
    /// Returns a compiled setter delegate for a property.
    /// </summary>
    Action<object, object?> GetOrCreateSetter(PropertyInfo prop);

    /// <summary>
    /// Materializes a TEntity from a data reader.
    /// </summary>
    TEntity MapReaderToObject(ITrackedReader reader);

    /// <summary>
    /// Appends a WHERE ... IN (...) clause to the SQL container for the given column.
    /// </summary>
    ISqlContainer BuildWhere(string wrappedColumnName, IEnumerable<TRowID> ids, ISqlContainer sqlContainer);

    /// <summary>
    /// Appends a composite primary key WHERE clause to the SQL container.
    /// </summary>
    void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc, string alias = "a");
}