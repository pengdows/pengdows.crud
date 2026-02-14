#region

using System.Data.Common;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Table gateway + mapper. SQL-first. No change tracking.
/// Primary table gateway interface for v2.0 and beyond.
/// </summary>
/// <remarks>
/// Reuses the CRUD generation, parameter binding, and mapping surface that existing helpers already depend on.
/// </remarks>
// 2.0 primary
public interface ITableGateway<TEntity, TRowID>
    where TEntity : class, new()
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
    /// Executes a SQL INSERT for the given object with cancellation support.
    /// Returns true when exactly one row was affected.
    /// </summary>
    Task<bool> CreateAsync(TEntity entity, IDatabaseContext context, CancellationToken cancellationToken);

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
    /// Builds a SQL SELECT for a list of row IDs (pseudo keys).
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
    /// Overload for retrieving by row IDs (pseudo keys) without alias.
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
    /// Builds an UPDATE statement asynchronously with cancellation support.
    /// </summary>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context,
        CancellationToken cancellationToken);

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
    /// Builds an UPDATE statement, optionally reloading the original, with cancellation support.
    /// </summary>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a DELETE by row identifier (pseudo key).
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
    /// Executes a DELETE for the given row identifier (pseudo key) and returns the number of affected rows.
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
    /// Executes a DELETE for the given row identifier with cancellation support.
    /// </summary>
    Task<int> DeleteAsync(TRowID id, IDatabaseContext? context, CancellationToken cancellationToken);

    /// <summary>
    /// Loads all entities matching the provided row IDs (pseudo keys).
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
    /// Loads all entities matching the provided row IDs with cancellation support.
    /// </summary>
    Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams entities matching the provided row IDs, yielding results as they are read from the database.
    /// This method is memory-efficient for large ID lists as it does not materialize the entire list in memory.
    /// </summary>
    /// <param name="ids">Collection of row IDs to retrieve</param>
    /// <param name="context">Optional database context. If null, uses the helper's default context.</param>
    /// <returns>An async stream of entities matching the provided IDs</returns>
    /// <remarks>
    /// <para>
    /// This method is ideal for processing large sets of entities without loading them all into memory at once.
    /// It internally builds a SELECT statement with the provided IDs and streams results using <see cref="LoadStreamAsync"/>.
    /// </para>
    /// <para>
    /// The stream can be enumerated multiple times, with each enumeration executing a new database query.
    /// Breaking from the enumeration early will dispose the reader and stop processing remaining results.
    /// </para>
    /// <para>
    /// Override <paramref name="context"/> only when executing within a transaction created from the parent database context.
    /// </para>
    /// </remarks>
    /// <example>
    /// Process a large number of entities without loading all into memory:
    /// <code>
    /// // Stream 10,000 orders without loading all into memory
    /// var orderIds = await GetAllOrderIdsAsync();
    /// await foreach (var order in helper.RetrieveStreamAsync(orderIds))
    /// {
    ///     await ProcessOrderAsync(order);
    ///
    ///     // Can break early without loading remaining orders
    ///     if (shouldStop)
    ///         break;
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);

    /// <summary>
    /// Streams entities matching the provided row IDs with cancellation support.
    /// </summary>
    /// <param name="ids">Collection of row IDs to retrieve</param>
    /// <param name="context">Optional database context. If null, uses the helper's default context.</param>
    /// <param name="cancellationToken">Cancellation token to observe during enumeration</param>
    /// <returns>An async stream of entities matching the provided IDs</returns>
    /// <remarks>
    /// This method supports cancellation via the provided token. The cancellation will be observed
    /// during database operations and iteration. Cancelling will dispose resources and stop enumeration.
    /// </remarks>
    /// <example>
    /// Stream with cancellation support:
    /// <code>
    /// var cts = new CancellationTokenSource();
    /// await foreach (var entity in helper.RetrieveStreamAsync(ids, null, cts.Token))
    /// {
    ///     await ProcessAsync(entity);
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes a DELETE for all provided row IDs (pseudo keys) and returns the number of affected rows.
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
    /// Executes a DELETE for all provided row IDs with cancellation support.
    /// </summary>
    Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context, CancellationToken cancellationToken);

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
    /// Executes an UPDATE for the given object with cancellation support.
    /// </summary>
    Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context, CancellationToken cancellationToken);

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
    /// Executes an UPDATE for the given object, optionally reloading the original, with cancellation support.
    /// </summary>
    Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context,
        CancellationToken cancellationToken);

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
    /// Inserts the entity if the ID is null or default, otherwise updates it, with cancellation support.
    /// </summary>
    Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context, CancellationToken cancellationToken);

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
    /// Loads a single object from the database using primary key values with cancellation support.
    /// </summary>
    Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context,
        CancellationToken cancellationToken);

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
    /// Loads a single object from the database using the row ID with cancellation support.
    /// </summary>
    Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context, CancellationToken cancellationToken);

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
    /// Loads a single object using a custom SQL container with cancellation support.
    /// </summary>
    Task<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken cancellationToken);

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
    /// Loads a list of objects using the provided SQL container with cancellation support.
    /// </summary>
    Task<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken cancellationToken);

    /// <summary>
    /// Streams objects using the provided SQL container, yielding results as they are read from the database.
    /// This method is memory-efficient for large result sets as it does not materialize the entire list in memory.
    /// </summary>
    /// <param name="sc">The SQL container with the query to execute.</param>
    /// <returns>An async enumerable stream of entities.</returns>
    /// <remarks>
    /// Use this method when processing large result sets to avoid loading all rows into memory at once.
    /// The underlying database reader remains open while enumerating, so ensure you consume the stream
    /// or dispose of it properly. Supports cancellation via the async enumerable pattern.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildBaseRetrieve("e");
    /// sc.Query.Append(" WHERE e.Status = 'Active'");
    ///
    /// await foreach (var entity in helper.LoadStreamAsync(sc))
    /// {
    ///     await ProcessEntityAsync(entity);
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc);

    /// <summary>
    /// Streams objects using the provided SQL container with cancellation support,
    /// yielding results as they are read from the database.
    /// This method is memory-efficient for large result sets as it does not materialize the entire list in memory.
    /// </summary>
    /// <param name="sc">The SQL container with the query to execute.</param>
    /// <param name="cancellationToken">Token to cancel the streaming operation.</param>
    /// <returns>An async enumerable stream of entities.</returns>
    /// <remarks>
    /// Use this method when processing large result sets to avoid loading all rows into memory at once.
    /// The underlying database reader remains open while enumerating, so ensure you consume the stream
    /// or dispose of it properly.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sc = helper.BuildBaseRetrieve("e");
    /// sc.Query.Append(" WHERE e.CreatedDate > @startDate");
    /// sc.AddParameterWithValue("startDate", DbType.DateTime, DateTime.UtcNow.AddDays(-30));
    ///
    /// await foreach (var entity in helper.LoadStreamAsync(sc, cancellationToken))
    /// {
    ///     await ProcessEntityAsync(entity, cancellationToken);
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken cancellationToken);

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

    // =========================================================================
    // Batch Operations
    // =========================================================================

    /// <summary>
    /// Builds one or more multi-row INSERT statements for the given entities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generates <c>INSERT INTO t (cols) VALUES (...), (...), (...)</c> statements.
    /// When the number of entities exceeds the dialect's parameter limit, the result
    /// is automatically chunked into multiple containers.
    /// </para>
    /// <para>
    /// Audit fields and version columns are set on each entity before SQL generation,
    /// following the same rules as <see cref="BuildCreate"/>.
    /// </para>
    /// </remarks>
    /// <param name="entities">The entities to insert. Must not be null.</param>
    /// <param name="context">Optional database context override for transaction scenarios.</param>
    /// <returns>One or more SQL containers, each representing a chunk of the batch.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entities"/> is null.</exception>
    /// <example>
    /// <code>
    /// var containers = helper.BuildBatchCreate(entities);
    /// foreach (var sc in containers)
    /// {
    ///     await sc.ExecuteNonQueryAsync();
    /// }
    /// </code>
    /// </example>
    IReadOnlyList<ISqlContainer> BuildBatchCreate(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a batch INSERT for the given entities and returns the total number of affected rows.
    /// </summary>
    /// <remarks>
    /// Empty lists return 0. Single-entity lists delegate to <see cref="CreateAsync(TEntity, IDatabaseContext)"/>.
    /// Multiple entities are chunked and executed sequentially.
    /// </remarks>
    /// <param name="entities">The entities to insert. Must not be null.</param>
    /// <param name="context">Optional database context override for transaction scenarios.</param>
    /// <returns>Total number of affected rows across all chunks.</returns>
    Task<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a batch INSERT for the given entities with cancellation support.
    /// </summary>
    Task<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds one or more provider-specific batch UPSERT statements for the given entities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL generated depends on the database dialect:
    /// </para>
    /// <list type="bullet">
    /// <item><description>PostgreSQL/CockroachDB: Multi-row <c>INSERT ... ON CONFLICT DO UPDATE</c></description></item>
    /// <item><description>MySQL/MariaDB: Multi-row <c>INSERT ... ON DUPLICATE KEY UPDATE</c></description></item>
    /// <item><description>SQL Server/Oracle/Firebird: Falls back to individual <see cref="BuildUpsert"/> per entity</description></item>
    /// </list>
    /// <para>
    /// Requires either <c>[PrimaryKey]</c> columns or a writable <c>[Id]</c> attribute for conflict detection.
    /// </para>
    /// </remarks>
    /// <param name="entities">The entities to upsert. Must not be null.</param>
    /// <param name="context">Optional database context override for transaction scenarios.</param>
    /// <returns>One or more SQL containers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entities"/> is null.</exception>
    /// <exception cref="NotSupportedException">Thrown when the entity has no suitable conflict key.</exception>
    IReadOnlyList<ISqlContainer> BuildBatchUpsert(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a batch UPSERT for the given entities and returns the total number of affected rows.
    /// </summary>
    Task<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a batch UPSERT for the given entities with cancellation support.
    /// </summary>
    Task<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context,
        CancellationToken cancellationToken);

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