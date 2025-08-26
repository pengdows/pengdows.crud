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
    /// <remarks>
    /// Creates an <see cref="ISqlContainer"/> without executing it, allowing callers to
    /// inspect or augment the generated command before running it. Override
    /// <paramref name="context"/> only when the command will execute inside a
    /// transaction derived from the parent database context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildCreate(entity);
    /// sc.Query.Append(" RETURNING Id");
    /// var rows = await sc.ExecuteNonQueryAsync();
    /// </code>
    /// </example>
    ISqlContainer BuildCreate(TEntity objectToCreate, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a SQL INSERT for the given object.
    /// Returns true when exactly one row was affected.
    /// </summary>
    Task<bool> CreateAsync(TEntity entity, IDatabaseContext context);

    /// <summary>
    /// Returns a SELECT clause with no WHERE clause.
    /// </summary>
    /// <remarks>
    /// Useful as a starting point when composing more complex queries. When
    /// <paramref name="alias"/> is provided, all column references are qualified
    /// with it; pass an empty string to omit aliasing. The returned container is
    /// not executed automatically; callers may append custom WHERE or ORDER BY
    /// clauses before execution. Provide <paramref name="context"/> only when the
    /// command will run within a transaction created from the parent context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildBaseRetrieve("e");
    /// sc.Query.Append(" WHERE e.IsActive = 1");
    /// var items = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
    ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null);

    /// <summary>
    /// Builds a SQL SELECT for a list of row IDs.
    /// </summary>
    /// <remarks>
    /// Use this overload when constructing a query that participates in a larger
    /// statement and therefore requires a table alias. The alias is applied to
    /// all generated column references. The returned container is not executed;
    /// append additional clauses as needed and then call <see cref="LoadListAsync"/>
    /// or <see cref="LoadSingleAsync"/>. Override <paramref name="context"/> only
    /// when executing inside a transaction derived from the parent context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildRetrieve(new[] { 1, 2, 3 }, "e");
    /// sc.Query.Append(" ORDER BY e.Name");
    /// var list = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds, string alias,
        IDatabaseContext? context = null);

    /// <summary>
    /// Builds a SQL SELECT for a list of object identities.
    /// </summary>
    /// <remarks>
    /// Similar to the ID-based overload, the alias is mandatory when the generated
    /// SQL needs to join with other tables or be embedded in a subquery. The
    /// returned container can be inspected or modified before execution. Pass
    /// <paramref name="context"/> only for queries that will run within a parent
    /// transaction.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildRetrieve(objectsToFind, "e");
    /// sc.Query.Append(" AND e.IsActive = 1");
    /// var list = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects, string alias,
        IDatabaseContext? context = null);

    /// <summary>
    /// Overload for retrieving by ID without alias.
    /// </summary>
    /// <remarks>
    /// Choose this overload for standalone queries where no table alias is required.
    /// The returned container is not executed, enabling inspection or further
    /// modification. Override <paramref name="context"/> only when executing inside
    /// a parent transaction.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildRetrieve(new[] { 1L, 2L });
    /// var items = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds, IDatabaseContext? context = null);

    /// <summary>
    /// Overload for retrieving by objects without alias.
    /// </summary>
    /// <remarks>
    /// Use when you already have entity instances and do not need to prefix
    /// generated columns with an alias. The resulting container can be adjusted
    /// before being executed against the database. Specify <paramref name="context"/>
    /// only if the command will run within a transaction created from the parent
    /// context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildRetrieve(objectsToFind);
    /// sc.Query.Append(" ORDER BY Name");
    /// var list = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects, IDatabaseContext? context = null);

    /// <summary>
    /// Builds an UPDATE statement asynchronously.
    /// </summary>
    /// <remarks>
    /// Generates SQL using the current values on <paramref name="objectToUpdate"/>
    /// without consulting the original database state. The returned container is
    /// not executed automatically, giving callers a chance to adjust the command
    /// before issuing it. Override <paramref name="context"/> only for execution
    /// within a transaction derived from the parent context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = await helper.BuildUpdateAsync(entity);
    /// sc.Query.Append("; SELECT @@ROWCOUNT");
    /// var rows = await sc.ExecuteNonQueryAsync();
    /// </code>
    /// </example>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null);

    /// <summary>
    /// Builds an UPDATE statement, optionally reloading the original.
    /// </summary>
    /// <remarks>
    /// Set <paramref name="loadOriginal"/> to <c>true</c> when the original
    /// persisted values are needed to compute the update statement. The resulting
    /// container is returned without being executed so that it can be inspected or
    /// modified. Supply <paramref name="context"/> only when running within a
    /// transaction created from the parent context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = await helper.BuildUpdateAsync(entity, loadOriginal: true);
    /// sc.Query.Append("; SELECT @@ROWCOUNT");
    /// var rows = await sc.ExecuteNonQueryAsync();
    /// </code>
    /// </example>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null);

    /// <summary>
    /// Builds a DELETE by primary key.
    /// </summary>
    /// <remarks>
    /// Returns a container representing the DELETE statement without executing it,
    /// allowing additional clauses to be appended. Override <paramref name="context"/>
    /// only when the command will execute within a transaction from the parent
    /// context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildDelete(42);
    /// var rows = await sc.ExecuteNonQueryAsync();
    /// </code>
    /// </example>
    ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a DELETE for the given primary key and returns the number of affected rows.
    /// </summary>
    /// <remarks>
    /// Use for deleting a single row by its identifier.
    /// </remarks>
    /// <example>
    /// <code>
    /// var rows = await helper.DeleteAsync(42);
    /// </code>
    /// </example>
    Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null);

    /// <summary>
    /// Loads all entities matching the provided IDs.
    /// </summary>
    /// <remarks>
    /// Convenience wrapper that builds a SELECT for <paramref name="ids"/> and
    /// internally calls <see cref="LoadListAsync"/>. Override
    /// <paramref name="context"/> only when executing within a transaction
    /// created from the parent database context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var entities = await helper.RetrieveAsync(new[] { 1, 2 });
    /// </code>
    /// </example>
    Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a DELETE for all provided IDs and returns the number of affected rows.
    /// </summary>
    /// <remarks>
    /// Allows batch deletion of multiple rows in a single statement.
    /// </remarks>
    /// <example>
    /// <code>
    /// var rows = await helper.DeleteAsync(new[] { 1, 2, 3 });
    /// </code>
    /// </example>
    Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);

    /// <summary>
    /// Executes an UPDATE for the given object and returns the number of affected rows.
    /// Returns 0 when no changes are detected.
    /// </summary>
    /// <remarks>
    /// Executes an UPDATE using the values currently on
    /// <paramref name="objectToUpdate"/> without reloading the original entity.
    /// </remarks>
    /// <example>
    /// <code>
    /// var rows = await helper.UpdateAsync(entity);
    /// </code>
    /// </example>
    Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null);

    /// <summary>
    /// Executes an UPDATE for the given object, optionally reloading the original,
    /// and returns the number of affected rows. Returns 0 when no changes are detected.
    /// </summary>
    /// <remarks>
    /// Setting <paramref name="loadOriginal"/> to <c>true</c> reloads the
    /// original row so that differences can be detected before executing the update.
    /// </remarks>
    /// <example>
    /// <code>
    /// var rows = await helper.UpdateAsync(entity, loadOriginal: true);
    /// </code>
    /// </example>
    Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null);

    /// <summary>
    /// Builds a provider-specific UPSERT statement.
    /// </summary>
    /// <remarks>
    /// Generates dialect-specific INSERT-or-UPDATE logic but does not execute it,
    /// enabling callers to inspect or tweak the resulting statement. Pass
    /// <paramref name="context"/> only when executing within a transaction of the
    /// parent context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildUpsert(entity);
    /// var rows = await sc.ExecuteNonQueryAsync();
    /// </code>
    /// </example>
    ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null);

    /// <summary>
    /// Inserts the entity if the ID is null or default, otherwise updates it. Returns the affected row count.
    /// </summary>
    Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null);

    /// <summary>
    /// Loads a single object from the database using primary key values.
    /// </summary>
    /// <remarks>
    /// Use when the entity has a composite key or when the values are already
    /// populated on an instance of <typeparamref name="TEntity"/>. Internally
    /// builds the query and delegates materialization to <see cref="LoadSingleAsync"/>.
    /// Supply <paramref name="context"/> only for transactions derived from the
    /// parent context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var found = await helper.RetrieveOneAsync(entity);
    /// </code>
    /// </example>
    Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null);

    /// <summary>
    /// Loads a single object from the database using the row ID.
    /// </summary>
    /// <remarks>
    /// Simpler overload when only the row ID is known. This convenience method
    /// builds the query and calls <see cref="LoadSingleAsync"/>. Override
    /// <paramref name="context"/> only when running inside a transaction from
    /// the parent context.
    /// </remarks>
    /// <example>
    /// <code>
    /// var found = await helper.RetrieveOneAsync(42);
    /// </code>
    /// </example>
    Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null);

    /// <summary>
    /// Loads a single object using a custom SQL container.
    /// </summary>
    /// <remarks>
    /// Executes the provided <paramref name="sc"/> and maps the first row into
    /// a <typeparamref name="TEntity"/>. Useful when you already have a SQL
    /// statement, such as one produced by <c>BuildRetrieve</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildRetrieve(new[] { id });
    /// var entity = await helper.LoadSingleAsync(sc);
    /// </code>
    /// </example>
    Task<TEntity?> LoadSingleAsync(ISqlContainer sc);

    /// <summary>
    /// Loads a list of objects using the provided SQL container.
    /// </summary>
    /// <remarks>
    /// Executes <paramref name="sc"/> and materializes each row via
    /// <see cref="MapReaderToObject"/>. Serves as the lower-level counterpart
    /// to <see cref="RetrieveAsync"/> when a custom query is required.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildRetrieve(new[] { 1, 2, 3 });
    /// var list = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
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
    /// <remarks>
    /// Useful for extending a custom <see cref="ISqlContainer"/> with an IN filter.
    /// This helper only mutates the provided container; it does not execute the
    /// command.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildBaseRetrieve("e");
    /// helper.BuildWhere("e.Id", new[] { 1, 2 }, sc);
    /// var rows = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
    ISqlContainer BuildWhere(string wrappedColumnName, IEnumerable<TRowID> ids, ISqlContainer sqlContainer);

    /// <summary>
    /// Appends a composite primary key WHERE clause to the SQL container.
    /// </summary>
    /// <remarks>
    /// Call when multiple key columns are required to identify rows. If
    /// <paramref name="alias"/> is provided, each column reference is qualified
    /// accordingly; otherwise the table name is used as-is. The method only
    /// augments <paramref name="sc"/>; execution remains the caller's
    /// responsibility.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildBaseRetrieve("e");
    /// helper.BuildWhereByPrimaryKey(entities, sc, "e");
    /// var list = await helper.LoadListAsync(sc);
    /// </code>
    /// </example>
    void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc, string alias = "");
}
