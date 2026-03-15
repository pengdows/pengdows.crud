#region

using System.Data.Common;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Table gateway for entities identified solely by <c>[PrimaryKey]</c> columns —
/// no surrogate <c>[Id]</c> column required.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="IPrimaryKeyTableGateway{TEntity}"/> as the entry point for tables that carry
/// only a natural/composite primary key and no surrogate identity column. All CRUD, batch,
/// and upsert operations use the <c>[PrimaryKey]</c> column(s) as the conflict / lookup key.
/// </para>
/// <para>
/// For tables that also carry a surrogate <c>[Id]</c> column use
/// <see cref="ITableGateway{TEntity,TRowID}"/> instead — it exposes both key styles.
/// </para>
/// </remarks>
/// <typeparam name="TEntity">Entity type mapped to the table. Must have a parameterless constructor.</typeparam>
public interface IPrimaryKeyTableGateway<TEntity>
    where TEntity : class, new()
{
    /// <summary>Fully qualified, quoted table name used by this entity.</summary>
    string WrappedTableName { get; }

    /// <summary>Determines what happens when enum parsing fails.</summary>
    EnumParseFailureMode EnumParseBehavior { get; set; }

    // =========================================================================
    // CREATE
    // =========================================================================

    /// <summary>Builds a SQL INSERT for the given entity without executing it.</summary>
    ISqlContainer BuildCreate(TEntity objectToCreate, IDatabaseContext? context = null);

    /// <summary>Executes a SQL INSERT. Returns true when exactly one row was affected.</summary>
    Task<bool> CreateAsync(TEntity entity);

    /// <summary>Executes a SQL INSERT with optional context and cancellation support.</summary>
    Task<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a list of entities. Delegates to <see cref="BatchCreateAsync"/>.
    /// </summary>
    Task<int> CreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchCreateAsync(entities, context, cancellationToken);

    // =========================================================================
    // RETRIEVE
    // =========================================================================

    /// <summary>Returns a SELECT clause with no WHERE clause.</summary>
    ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null);

    /// <summary>
    /// Returns a SELECT clause with no WHERE clause, including extra projected expressions.
    /// </summary>
    ISqlContainer BuildBaseRetrieve(string alias, IReadOnlyCollection<string> extraSelectExpressions,
        IDatabaseContext? context = null);

    /// <summary>
    /// Builds a SELECT … WHERE [PrimaryKey] IN (…) for the given entity list.
    /// </summary>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects, string alias,
        IDatabaseContext? context = null);

    /// <summary>
    /// Builds a SELECT … WHERE [PrimaryKey] IN (…) for the given entity list, without alias.
    /// </summary>
    ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
        IDatabaseContext? context = null);

    /// <summary>
    /// Retrieves a single entity by its <c>[PrimaryKey]</c> column values.
    /// </summary>
    Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // UPDATE
    // =========================================================================

    /// <summary>
    /// Builds an UPDATE statement. WHERE clause is built from <c>[PrimaryKey]</c> columns.
    /// </summary>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds an UPDATE statement, optionally reloading the original row first.
    /// </summary>
    Task<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an UPDATE keyed on <c>[PrimaryKey]</c> columns. Returns rows affected.
    /// </summary>
    Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an UPDATE, optionally reloading original. Returns rows affected.
    /// </summary>
    Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a list of entities. Delegates to <see cref="BatchUpdateAsync"/>.
    /// </summary>
    Task<int> UpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchUpdateAsync(entities, context, cancellationToken);

    // =========================================================================
    // DELETE
    // =========================================================================

    /// <summary>
    /// Builds one or more DELETE statements for the given entities keyed on <c>[PrimaryKey]</c>.
    /// </summary>
    IReadOnlyList<ISqlContainer> BuildBatchDelete(IReadOnlyCollection<TEntity> entities,
        IDatabaseContext? context = null);

    /// <summary>
    /// Executes DELETE for all given entities (by <c>[PrimaryKey]</c>). Returns rows affected.
    /// </summary>
    Task<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the given entities by <c>[PrimaryKey]</c>. Delegates to
    /// <see cref="BatchDeleteAsync(IReadOnlyCollection{TEntity}, IDatabaseContext?, CancellationToken)"/>.
    /// </summary>
    Task<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchDeleteAsync(entities, context, cancellationToken);

    // =========================================================================
    // UPSERT
    // =========================================================================

    /// <summary>
    /// Builds a provider-specific UPSERT using <c>[PrimaryKey]</c> columns as the conflict key.
    /// </summary>
    ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null);

    /// <summary>
    /// Executes a provider-specific UPSERT keyed on <c>[PrimaryKey]</c> columns.
    /// </summary>
    Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a list of entities. Delegates to <see cref="BatchUpsertAsync"/>.
    /// </summary>
    Task<int> UpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchUpsertAsync(entities, context, cancellationToken);

    // =========================================================================
    // LOAD (execute a pre-built container)
    // =========================================================================

    /// <summary>Executes the container and returns the first mapped entity, or null.</summary>
    Task<TEntity?> LoadSingleAsync(ISqlContainer sc);

    /// <inheritdoc cref="LoadSingleAsync(ISqlContainer)"/>
    Task<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken cancellationToken);

    /// <summary>Executes the container and returns all mapped entities.</summary>
    Task<List<TEntity>> LoadListAsync(ISqlContainer sc);

    /// <inheritdoc cref="LoadListAsync(ISqlContainer)"/>
    Task<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken cancellationToken);

    /// <summary>Streams entities from the container without materializing the full result set.</summary>
    IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc);

    /// <inheritdoc cref="LoadStreamAsync(ISqlContainer)"/>
    IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken cancellationToken);

    // =========================================================================
    // BATCH
    // =========================================================================

    /// <summary>Builds one or more multi-row INSERT statements for the given entities.</summary>
    IReadOnlyList<ISqlContainer> BuildBatchCreate(IReadOnlyList<TEntity> entities,
        IDatabaseContext? context = null);

    /// <summary>Executes a batch INSERT. Returns total rows affected.</summary>
    Task<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>Builds one or more batch UPDATE statements keyed on <c>[PrimaryKey]</c>.</summary>
    IReadOnlyList<ISqlContainer> BuildBatchUpdate(IReadOnlyList<TEntity> entities,
        IDatabaseContext? context = null);

    /// <summary>Executes a batch UPDATE keyed on <c>[PrimaryKey]</c>. Returns total rows affected.</summary>
    Task<int> BatchUpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>Builds one or more batch UPSERT statements keyed on <c>[PrimaryKey]</c>.</summary>
    IReadOnlyList<ISqlContainer> BuildBatchUpsert(IReadOnlyList<TEntity> entities,
        IDatabaseContext? context = null);

    /// <summary>Executes a batch UPSERT keyed on <c>[PrimaryKey]</c>. Returns total rows affected.</summary>
    Task<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>
    /// Appends a WHERE clause to <paramref name="sc"/> using <c>[PrimaryKey]</c> column values
    /// from each entity in <paramref name="listOfObjects"/>.
    /// </summary>
    void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc,
        string alias = "");

    /// <summary>Returns a compiled setter delegate for a property.</summary>
    Action<object, object?> GetOrCreateSetter(PropertyInfo prop);

    /// <summary>Materializes a <typeparamref name="TEntity"/> from a data reader row.</summary>
    TEntity MapReaderToObject(ITrackedReader reader);
}
