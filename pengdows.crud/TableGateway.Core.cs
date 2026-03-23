// =============================================================================
// FILE: TableGateway.Core.cs
// PURPOSE: Core partial of TableGateway - the primary CRUD API for entities.
//          Contains constructor, initialization, and core infrastructure.
//
// AI SUMMARY:
// - TableGateway<TEntity, TRowID> is the main API for entity CRUD operations.
// - Replaces the older TableGateway<> name (which is now a compatibility shim).
// - This partial contains:
//   * Static initialization and TRowID type validation
//   * Constructor that takes DatabaseContext and optional AuditValueResolver
//   * Core field declarations (context, dialect, tableInfo, caches)
//   * BoundedCache instances for query templates and column lists
// - Partial class structure:
//   * Core.cs - This file (initialization, fields)
//   * Audit.cs - Audit column handling
//   * Caching.cs - Template caching
//   * Reader.cs - DataReader mapping
//   * Retrieve.cs - SELECT operations
//   * Sql.cs - SQL generation helpers
//   * Update.cs - UPDATE operations
//   * Upsert.cs - UPSERT operations
// - TRowID validation: Must be primitive integer, Guid, or string.
// - Thread-safe: Uses bounded caches and thread-safe data structures.
// - Uses TypeMapRegistry to get entity metadata (TableInfo).
// =============================================================================

#region

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Primary SQL-first CRUD gateway for table-mapped entities.
/// Provides SQL generation and CRUD operations for entities mapped to database tables.
/// </summary>
/// <typeparam name="TEntity">The entity type to operate on.</typeparam>
/// <typeparam name="TRowID">The row ID type (must be primitive integer, Guid, or string).</typeparam>
/// <remarks>
/// <para>
/// This is the primary table gateway API. The legacy <c>TableGateway&lt;TEntity, TRowID&gt;</c>
/// type remains as a compatibility shim that inherits from this class.
/// </para>
/// </remarks>
public partial class TableGateway<TEntity, TRowID> :
    BaseTableGateway<TEntity>,
    ITableGateway<TEntity, TRowID> where TEntity : class, new()
{
    private const string EmptyIdListMessage = "List of IDs cannot be empty.";
    private const string UpsertNoKeyMessage = "Upsert requires an Id or a composite primary key.";
    private const string UpsertNoWritableKeyMessage = "Upsert requires client-assigned Id or [PrimaryKey] attributes.";

    static TableGateway()
    {
        ValidateRowIdType();
    }

    private IColumnInfo? _idColumn;

    // Monolithic parameter binders, cached per dialect
    private readonly ConcurrentDictionary<SupportedDatabase, CompiledBinderFactory<TEntity>.Binder> _insertBinders = new();
    private readonly ConcurrentDictionary<SupportedDatabase, CompiledBinderFactory<TEntity>.Binder> _upsertBinders = new();
    private readonly ConcurrentDictionary<SupportedDatabase, CompiledBinderFactory<TEntity>.UpdateBinder> _updateBinders = new();

    // Per-dialect templates are cached in _templatesByDialect

    // SQL templates cached per dialect to support context overrides
    private readonly ConcurrentDictionary<SupportedDatabase, Lazy<CachedSqlTemplates>> _templatesByDialect = new();

    // Pre-built SqlContainer cache for common operations (GetById, GetByIds, etc.)
    private readonly ConcurrentDictionary<SupportedDatabase, Lazy<CachedContainerTemplates>> _containersByDialect =
        new();


    // Unified constructor accepting optional audit resolver and optional logger (by name)
    public TableGateway(IDatabaseContext databaseContext,
        IAuditValueResolver? auditValueResolver = null,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw,
        ILogger? logger = null)
        : base(databaseContext, auditValueResolver, enumParseBehavior, logger)
    {
        // Id-specific initialization: locate the [Id] column after base Initialize()
        _idColumn = _tableInfo.Columns.Values.FirstOrDefault(itm => itm.IsId);
    }


    /// <inheritdoc/>
    public ValueTask<bool> CreateAsync(TEntity entity)
    {
        return CreateAsync(entity, _context);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var plan = dialect.GetGeneratedKeyPlan();

        // 1. Handle PREFETCH plans (Oracle)
        if (plan == GeneratedKeyPlan.PrefetchSequence && _idColumn != null)
        {
            var seqQuery = dialect.GetSequenceNextValQuery(GetSequenceName());
            using var seqSc = ctx.CreateSqlContainer(seqQuery);
            var nextVal = await seqSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Read).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(nextVal, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);

            // Proceed with standard insert since ID is now populated
            await using var sc = BuildCreate(entity, ctx);
            return await sc.ExecuteNonQueryAsync().ConfigureAwait(false) == 1;
        }

        // 2. Handle INLINE plans (Postgres, SQL Server, etc.)
        if ((plan == GeneratedKeyPlan.Returning || plan == GeneratedKeyPlan.OutputInserted) &&
            _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var sc = BuildCreateWithReturning(entity, true, ctx);

            object? generatedId;
            if (dialect.DatabaseType == SupportedDatabase.Oracle)
            {
                await sc.ExecuteNonQueryAsync(ExecutionType.Write).ConfigureAwait(false);
                generatedId = sc.GetParameterValue(OracleReturningParameterName);
            }
            else
            {
                generatedId = await sc.ExecuteScalarOrNullAsync<object>(ExecutionType.Write).ConfigureAwait(false);
            }

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var targetType = _idColumn.PropertyInfo.PropertyType;
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, targetType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            // Fallback: PopulateGeneratedIdAsync for test/fake scenarios
            await PopulateGeneratedIdAsync(entity, ctx).ConfigureAwait(false);
            return true;
        }

        // 3. Handle CORRELATION TOKEN plan
        if (plan == GeneratedKeyPlan.CorrelationToken && _tableInfo.CorrelationColumn != null && _idColumn != null)
        {
            var token = Guid.NewGuid().ToString("N");
            _tableInfo.CorrelationColumn.PropertyInfo.SetValue(entity, token);

            await using var sc = BuildCreate(entity, ctx);
            if (await sc.ExecuteNonQueryAsync().ConfigureAwait(false) != 1)
            {
                return false;
            }

            var lookupSql = dialect.GetCorrelationTokenLookupQuery(
                _tableInfo.Name,
                _idColumn.Name,
                _tableInfo.CorrelationColumn.Name,
                dialect.MakeParameterName("p1"));

            using var lookupSc = ctx.CreateSqlContainer(lookupSql);
            lookupSc.AddParameterWithValue("p1", DbType.String, token);

            var generatedId = await lookupSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Read).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);
            return true;
        }

        // 4. Compound statement plan (MySQL, MariaDB, SQLite pre-3.35).
        // Appends the dialect's session-scoped ID query (e.g. "; SELECT LAST_INSERT_ID()")
        // to the INSERT and executes both as a single batch on one connection.
        // This fixes the two-lease hazard: LAST_INSERT_ID() / last_insert_rowid() are
        // session-scoped; a separate pool lease could return a stale or zero value.
        if (plan == GeneratedKeyPlan.CompoundStatement && _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var sc = BuildCreate(entity, ctx);
            sc.Query.Append(dialect.GetCompoundInsertIdSuffix());

            // Scope the reader to this block so the connection is released before
            // PopulateGeneratedIdAsync (which opens its own connection on pool/SingleConnection).
            object? generatedId = null;
            await using (var reader = await sc.ExecuteReaderAsync(ExecutionType.Write).ConfigureAwait(false))
            {
                // First result set = INSERT (rows-affected, no data rows).
                // Advance to the SELECT result set to read the generated ID.
                // Use IInternalTrackedReader.InnerReader to bypass TrackedReader.NextResult() policy;
                // the policy blocks multi-result for general use, but the compound path reads all
                // result sets before disposing so the connection lifecycle is correctly managed.
                if (reader is IInternalTrackedReader internalReader)
                {
                    var inner = internalReader.InnerReader;
                    if (await inner.NextResultAsync().ConfigureAwait(false) &&
                        await inner.ReadAsync().ConfigureAwait(false))
                    {
                        generatedId = inner[0];
                    }
                }
            } // reader disposed here — connection released before any fallback query

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            // Fallback for providers/fakeDb that return false from NextResult()
            // (e.g. fakeDbDataReader.NextResult always returns false).
            await PopulateGeneratedIdAsync(entity, ctx).ConfigureAwait(false);
            return true;
        }

        // 5. Default path: standard insert followed by optional session-scoped retrieval
        {
            await using var sc = BuildCreate(entity, ctx);
            var rowsAffected = await sc.ExecuteNonQueryAsync().ConfigureAwait(false);

            if (rowsAffected == 1 && _idColumn != null && !_idColumn.IsIdWritable)
            {
                await PopulateGeneratedIdAsync(entity, ctx).ConfigureAwait(false);
            }

            return rowsAffected == 1;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var plan = dialect.GetGeneratedKeyPlan();

        // 1. Handle PREFETCH plans (Oracle)
        if (plan == GeneratedKeyPlan.PrefetchSequence && _idColumn != null)
        {
            var seqQuery = dialect.GetSequenceNextValQuery(GetSequenceName());
            using var seqSc = ctx.CreateSqlContainer(seqQuery);
            var nextVal = await seqSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Read, CommandType.Text, cancellationToken).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(nextVal, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);

            await using var sc = BuildCreate(entity, ctx);
            return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false) == 1;
        }

        // 2. Handle INLINE plans (Postgres, SQL Server, etc.)
        if ((plan == GeneratedKeyPlan.Returning || plan == GeneratedKeyPlan.OutputInserted) &&
            _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var sc = BuildCreateWithReturning(entity, true, ctx);

            object? generatedId;
            if (dialect.DatabaseType == SupportedDatabase.Oracle)
            {
                await sc.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, cancellationToken)
                    .ConfigureAwait(false);
                generatedId = sc.GetParameterValue(OracleReturningParameterName);
            }
            else
            {
                generatedId = await sc
                    .ExecuteScalarOrNullAsync<object>(ExecutionType.Write, CommandType.Text, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            // Fallback: PopulateGeneratedIdAsync for test/fake scenarios
            await PopulateGeneratedIdAsync(entity, ctx, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // 3. Handle CORRELATION TOKEN plan
        if (plan == GeneratedKeyPlan.CorrelationToken && _tableInfo.CorrelationColumn != null && _idColumn != null)
        {
            var token = Guid.NewGuid().ToString("N");
            _tableInfo.CorrelationColumn.PropertyInfo.SetValue(entity, token);

            await using var sc = BuildCreate(entity, ctx);
            if (await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false) != 1)
            {
                return false;
            }

            var lookupSql = dialect.GetCorrelationTokenLookupQuery(
                _tableInfo.Name,
                _idColumn.Name,
                _tableInfo.CorrelationColumn.Name,
                dialect.MakeParameterName("p1"));

            using var lookupSc = ctx.CreateSqlContainer(lookupSql);
            lookupSc.AddParameterWithValue("p1", DbType.String, token);

            var generatedId = await lookupSc.ExecuteScalarRequiredAsync<object>(ExecutionType.Read, CommandType.Text, cancellationToken).ConfigureAwait(false);
            var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
            _idColumn.PropertyInfo.SetValue(entity, converted);
            return true;
        }

        // 4. Compound statement plan (MySQL, MariaDB, SQLite pre-3.35).
        if (plan == GeneratedKeyPlan.CompoundStatement && _idColumn != null && !_idColumn.IsIdWritable)
        {
            await using var sc = BuildCreate(entity, ctx);
            sc.Query.Append(dialect.GetCompoundInsertIdSuffix());

            // Scope the reader to release the connection before PopulateGeneratedIdAsync.
            object? generatedId = null;
            await using (var reader = await sc.ExecuteReaderAsync(ExecutionType.Write, CommandType.Text, cancellationToken).ConfigureAwait(false))
            {
                if (reader is IInternalTrackedReader internalReader)
                {
                    var inner = internalReader.InnerReader;
                    if (await inner.NextResultAsync(cancellationToken).ConfigureAwait(false) &&
                        await inner.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        generatedId = inner[0];
                    }
                }
            } // reader disposed here — connection released before any fallback query

            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }

            await PopulateGeneratedIdAsync(entity, ctx, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // 5. Default path: standard insert followed by optional session-scoped retrieval
        {
            await using var sc = BuildCreate(entity, ctx);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
            if (rowsAffected == 1 && _idColumn != null && !_idColumn.IsIdWritable)
            {
                await PopulateGeneratedIdAsync(entity, ctx, cancellationToken).ConfigureAwait(false);
            }

            return rowsAffected == 1;
        }
    }

    private async Task PopulateGeneratedIdAsync(TEntity entity, IDatabaseContext context,
        CancellationToken cancellationToken = default)
    {
        if (_idColumn == null)
        {
            return;
        }

        // Get the database-specific query for retrieving the last inserted ID
        var ctx = context ?? _context;
        string lastIdQuery;
        try
        {
            lastIdQuery = ctx.GetDialect().GetLastInsertedIdQuery();
        }
        catch (NotSupportedException)
        {
            // Some databases (Oracle, Unknown) don't support generic last-insert-id queries
            // This is expected behavior - just skip ID population
            return;
        }

        if (string.IsNullOrEmpty(lastIdQuery))
        {
            return;
        }

        await using var sc = ctx.CreateSqlContainer(lastIdQuery);
        var generatedId = await sc.ExecuteScalarOrNullAsync<object>(CommandType.Text, cancellationToken);

        if (generatedId != null && generatedId != DBNull.Value)
        {
            try
            {
                var targetType = _idColumn.PropertyInfo.PropertyType;
                if (targetType == typeof(Guid))
                {
                    if (generatedId is Guid g)
                    {
                        _idColumn.PropertyInfo.SetValue(entity, g);
                    }
                    else if (Guid.TryParse(generatedId.ToString(), out var parsed))
                    {
                        _idColumn.PropertyInfo.SetValue(entity, parsed);
                    }
                    else
                    {
                        throw new InvalidCastException("Unable to convert generated ID to Guid.");
                    }
                }
                else
                {
                    // Convert the ID to the appropriate type and set it on the entity
                    var convertedId = TypeCoercionHelper.ConvertWithCache(generatedId, targetType);
                    _idColumn.PropertyInfo.SetValue(entity, convertedId);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to convert generated ID '{generatedId}' to type {_idColumn.PropertyInfo.PropertyType.Name}: {ex.Message}",
                    ex);
            }
        }
    }


    // Placeholders for identity-returning clauses in INSERT statements
    private const string OutputClausePlaceholder = "{output}"; // SQL Server: OUTPUT INSERTED.id (before VALUES)

    private const string
        ReturningClausePlaceholder = "{returning}"; // PostgreSQL/SQLite/etc: RETURNING id (after VALUES)

    private const string OracleReturningParameterName = "o0";

    /// <inheritdoc/>
    public ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null)
    {
        var (sc, _) = PrepareInsertContainer(entity, context, stripPlaceholders: true);
        return sc;
    }

    /// <summary>
    /// Prepares the SqlContainer with an INSERT statement.
    /// When stripPlaceholders is true, identity clause placeholders are removed and the cached
    /// template is cloned for better performance. When false, placeholders remain for
    /// BuildCreateWithReturning to replace.
    /// </summary>
    private (ISqlContainer sc, ISqlDialect dialect) PrepareInsertContainer(TEntity entity, IDatabaseContext? context,
        bool stripPlaceholders)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        // Mutate entity first (same for both paths)
        MutateEntityForInsert(entity);

        var sqlTemplate = GetTemplatesForDialect(dialect);

        // Fast path: clone from cached InsertTemplate (skips SQL string building)
        if (stripPlaceholders)
        {
            var containerTemplates = GetContainerTemplatesForDialect(dialect, ctx);
            var sc = containerTemplates.InsertTemplate.Clone(ctx);

            // Optimized monolithic binding: bypass sc.SetParameterValue dictionary lookups.
            // We clear the cloned parameters and re-add them using the fast binder.
            sc.Clear();
            ((SqlQueryBuilder)sc.Query).CopyFrom((SqlQueryBuilder)containerTemplates.InsertTemplate.Query); // Restore query after Clear()

            var binder = GetOrBuildInsertBinder(dialect, sqlTemplate);
            var parameters = new List<DbParameter>(sqlTemplate.InsertColumns.Count);
            binder(entity, parameters);
            sc.AddParameters(parameters);

            return (sc, dialect);
        }

        // Slow path: build full SQL with placeholders for BuildCreateWithReturning
        return BuildInsertContainerDirect(entity, ctx, dialect, sqlTemplate);
    }

    /// <summary>
    /// Applies entity mutations required before INSERT: ID generation, audit fields, version init.
    /// </summary>
    private void MutateEntityForInsert(TEntity entity)
    {
        EnsureWritableIdHasValue(entity);

        if (_hasAuditColumns)
        {
            SetAuditFields(entity, false);
        }

        if (_versionColumn != null)
        {
            var current = _versionColumn.MakeParameterValueFromField(entity);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                             _versionColumn.PropertyInfo.PropertyType;
                if (Utils.IsZeroNumeric(TypeCoercionHelper.ConvertWithCache(0, target)))
                {
                    var one = TypeCoercionHelper.ConvertWithCache(1, target);
                    _versionColumn.PropertyInfo.SetValue(entity, one);
                }
            }
        }
    }

    /// <summary>
    /// Builds INSERT SQL container directly (no template cloning). Used during template
    /// initialization and for BuildCreateWithReturning which needs placeholder tokens.
    /// </summary>
    private (ISqlContainer sc, ISqlDialect dialect) BuildInsertContainerDirect(
        TEntity entity, IDatabaseContext ctx, ISqlDialect dialect, CachedSqlTemplates sqlTemplate)
    {
        var sc = ctx.CreateSqlContainer();

        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (");

        for (var i = 0; i < sqlTemplate.InsertColumns.Count; i++)
        {
            var column = sqlTemplate.InsertColumns[i];
            var value = column.MakeParameterValueFromField(entity);

            var paramName = sqlTemplate.InsertParameterNames[i];
            var param = dialect.CreateDbParameter(paramName, column.DbType, value);
            if (column.IsJsonType)
            {
                dialect.TryMarkJsonParameter(param, column);
            }

            sc.AddParameter(param);

            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapSimpleName(column.Name));
        }

        // Insert OUTPUT placeholder (for SQL Server) between column list and VALUES
        sc.Query.Append(')')
            .Append(OutputClausePlaceholder)
            .Append(" VALUES (");

        for (var i = 0; i < sqlTemplate.InsertColumns.Count; i++)
        {
            var column = sqlTemplate.InsertColumns[i];
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            var paramName = sqlTemplate.InsertParameterNames[i];
            if (column.IsJsonType)
            {
                sc.Query.Append(dialect.RenderJsonArgument(dialect.MakeParameterName(paramName), column));
            }
            else
            {
                sc.Query.Append(dialect.MakeParameterName(paramName));
            }
        }

        // Insert RETURNING placeholder (for PostgreSQL/SQLite/etc) after VALUES
        sc.Query.Append(')')
            .Append(ReturningClausePlaceholder);

        return (sc, dialect);
    }

    private void EnsureWritableIdHasValue(TEntity entity)
    {
        if (_idColumn == null || !_idColumn.IsIdWritable)
        {
            return;
        }

        var property = _idColumn.PropertyInfo;
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var currentValue = _idColumn.MakeParameterValueFromField(entity);

        if (underlyingType == typeof(Guid))
        {
            var currentGuid = currentValue switch
            {
                Guid g => g,
                _ => Guid.Empty
            };

            if (currentGuid == Guid.Empty)
            {
                var newGuid = Guid.NewGuid();
                property.SetValue(entity, property.PropertyType == typeof(Guid?) ? (Guid?)newGuid : newGuid);
            }
        }
        else if (underlyingType == typeof(string))
        {
            var currentString = currentValue as string;
            if (string.IsNullOrWhiteSpace(currentString))
            {
                var generated = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
                property.SetValue(entity, generated);
            }
        }
    }

    /// <summary>
    /// Builds an INSERT statement with optional RETURNING/OUTPUT clause for identity capture.
    /// Uses placeholder replacement for clean, predictable SQL generation.
    /// </summary>
    /// <param name="entity">Entity to insert</param>
    /// <param name="withReturning">Whether to include RETURNING/OUTPUT clause for identity</param>
    /// <param name="context">Database context</param>
    /// <returns>SQL container with INSERT statement</returns>
    public ISqlContainer BuildCreateWithReturning(TEntity entity, bool withReturning, IDatabaseContext? context = null)
    {
        var (sc, dialect) = PrepareInsertContainer(entity, context, stripPlaceholders: false);

        var outputClause = string.Empty;
        var returningClause = string.Empty;

        if (withReturning && _idColumn != null && !_idColumn.IsIdWritable && dialect.SupportsInsertReturning)
        {
            var idWrapped = dialect.WrapSimpleName(_idColumn.Name);
            var clause = dialect.RenderInsertReturningClause(idWrapped);

            if (dialect.DatabaseType == SupportedDatabase.SqlServer)
            {
                outputClause = clause; // SQL Server: OUTPUT goes before VALUES
            }
            else if (dialect.DatabaseType == SupportedDatabase.Oracle)
            {
                returningClause = clause.Replace("?", dialect.MakeParameterName(OracleReturningParameterName),
                    StringComparison.Ordinal);
                sc.AddParameterWithValue<object?>(OracleReturningParameterName, _idColumn.DbType, null,
                    ParameterDirection.Output);
            }
            else
            {
                returningClause = clause; // Others: RETURNING goes after VALUES
            }
        }

        // Replace placeholders with actual clauses (or empty strings)
        sc.Query.Replace(OutputClausePlaceholder, outputClause);
        sc.Query.Replace(ReturningClausePlaceholder, returningClause);

        return sc;
    }

    // moved to TableGateway.Retrieve.cs

    /// <inheritdoc/>
    public ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                $"row identity column for table {BuildWrappedTableName(dialect)} not found");
        }

        var containerTemplates = GetContainerTemplatesForDialect(dialect, ctx);
        var template = containerTemplates.DeleteByIdTemplate;
        if (template != null)
        {
            var sc = template.Clone(ctx);
            sc.SetParameterValue("k0", id);
            return sc;
        }

        return BuildDeleteDirect(id, ctx);
    }

    /// <summary>
    /// Builds DELETE SQL directly without using cached container templates.
    /// Used during template initialization to avoid circular dependency.
    /// </summary>
    private ISqlContainer BuildDeleteDirect(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);

        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                $"row identity column for table {BuildWrappedTableName(dialect)} not found");
        }

        var counters = new ClauseCounters();
        var name = counters.NextKey();
        var param = dialect.CreateDbParameter(name, _idColumn.DbType, id);
        sc.AddParameter(param);

        var deleteCache = GetOrCreateQueryCache(dialect);
        if (!deleteCache.TryGet("DeleteById", out var sql))
        {
            sql = string.Format(GetTemplatesForDialect(dialect).DeleteSql, dialect.MakeParameterName(param));
            deleteCache.GetOrAdd("DeleteById", _ => sql);
        }

        sc.Query.Append(sql);
        return sc;
    }

    /// <inheritdoc/>
    public async ValueTask<int> DeleteAsync(TRowID id, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        await using var sc = BuildDelete(id, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            throw new ArgumentException(EmptyIdListMessage, nameof(ids));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        if (!dialect.SupportsSetValuedParameters && ctx.MaxParameterLimit > 0 && list.Count > ctx.MaxParameterLimit)
        {
            var results = new List<TEntity>(list.Count);
            var limit = ctx.MaxParameterLimit;
            for (var offset = 0; offset < list.Count; offset += limit)
            {
                var count = Math.Min(limit, list.Count - offset);
                var chunk = list.GetRange(offset, count);
                await using var sc = BuildRetrieve(chunk, ctx);
                var chunkResults = await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
                results.AddRange(chunkResults);
            }

            return results;
        }

        // Try to use cached templates for better performance, but fall back to traditional method
        // to avoid circular dependency during template building
        try
        {
            // For small lists, use cached template for better performance
            // For larger lists, fall back to BuildRetrieve to handle dynamic parameter lists correctly
            if (list.Count == 1)
            {
                // Single ID - reuse GetByIdTemplate
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                using var container = templates.GetByIdTemplate!.Clone(ctx);

                if (dialect.SupportsSetValuedParameters)
                {
                    container.SetParameterValue("p0", list.ToArray());
                }
                else
                {
                    container.SetParameterValue("p0", list[0]);
                }

                return await LoadListAsync(container, cancellationToken).ConfigureAwait(false);
            }

            if (list.Count == 2 && !dialect.SupportsSetValuedParameters)
            {
                // Two IDs - can reuse GetByIdsTemplate for non-array dialects
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                using var container = templates.GetByIdsTemplate!.Clone(ctx);

                container.SetParameterValue("p0", list[0]);
                container.SetParameterValue("p1", list[1]);

                return await LoadListAsync(container, cancellationToken).ConfigureAwait(false);
            }

            // For n>2: BuildRetrieve handles dialect-specific parameterization internally
            await using var sc = BuildRetrieve(list, ctx);
            return await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
        }
        catch (exceptions.TemplateInitializationException)
        {
            // Template build failed for this dialect; fall back to direct BuildRetrieve path
            await using var sc = BuildRetrieve(list, ctx);
            return await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids,
        IDatabaseContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            // Empty ID list - return empty stream
            yield break;
        }

        var ctx = context ?? _context;

        // Get the container to use (with try-catch for error handling)
        await using var container = GetRetrieveContainer(list, ctx);

        // Stream results from the container
        await foreach (var entity in LoadStreamAsync(container, cancellationToken).ConfigureAwait(false))
        {
            yield return entity;
        }
    }

    private ISqlContainer GetRetrieveContainer(IReadOnlyList<TRowID> list, IDatabaseContext ctx)
    {
        var dialect = GetDialect(ctx);

        // Try to use cached templates for better performance, but fall back to traditional method
        // to avoid circular dependency during template building
        try
        {
            // For small lists, use cached template for better performance
            // For larger lists, fall back to BuildRetrieve to handle dynamic parameter lists correctly
            if (list.Count == 1)
            {
                // Single ID - reuse GetByIdTemplate
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                var container = templates.GetByIdTemplate!.Clone(ctx);

                if (dialect.SupportsSetValuedParameters)
                {
                    container.SetParameterValue("p0", list.ToArray());
                }
                else
                {
                    container.SetParameterValue("p0", list[0]);
                }

                return container;
            }

            if (list.Count == 2 && !dialect.SupportsSetValuedParameters)
            {
                // Two IDs - can reuse GetByIdsTemplate for non-array dialects
                var templates = GetContainerTemplatesForDialect(dialect, ctx);
                var container = templates.GetByIdsTemplate!.Clone(ctx);

                container.SetParameterValue("p0", list[0]);
                container.SetParameterValue("p1", list[1]);

                return container;
            }

            // Fall back to dynamic BuildRetrieve for larger lists
            return BuildRetrieve(list, ctx);
        }
        catch (exceptions.TemplateInitializationException)
        {
            // Template build failed for this dialect; fall back to direct BuildRetrieve path
            return BuildRetrieve(list, ctx);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchDelete(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            throw new ArgumentException(EmptyIdListMessage, nameof(ids));
        }

        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        var dialect = GetDialect(ctx);
        var wrappedIdColumnName = dialect.WrapSimpleName(_idColumn.Name);

        // Chunk by max parameter limit (with 10% headroom, similar to BatchCreate)
        var chunks = ChunkList(list, 1, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("DELETE FROM ").Append(BuildWrappedTableName(dialect));
            BuildWhere(wrappedIdColumnName, chunk, sc);
            result.Add(sc);
        }

        return result;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public async ValueTask<int> BatchDeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var containers = BuildBatchDelete(ids, context);
        var totalAffected = 0;

        foreach (var sc in containers)
        {
            await using var owned = sc;
            cancellationToken.ThrowIfCancellationRequested();
            totalAffected += await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        return totalAffected;
    }

    /// <inheritdoc/>
    public ValueTask<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchDeleteAsync(ids, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchDeleteAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchDelete(IReadOnlyCollection<TEntity> entities,
        IDatabaseContext? context = null)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (entities.Count == 0)
        {
            return Array.Empty<ISqlContainer>();
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var keys = GetPrimaryKeys();

        // Chunk by Math.Floor(maxParameterLimit / numberOfPrimaryKeys) with 10% headroom
        var chunks = ChunkList(entities.ToList(), keys.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = ctx.CreateSqlContainer();
            sc.Query.Append("DELETE FROM ").Append(BuildWrappedTableName(dialect));
            BuildWhereByPrimaryKey(chunk, sc, "", dialect);
            result.Add(sc);
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (entities.Count == 0)
        {
            return 0;
        }

        var containers = BuildBatchDelete(entities, context);
        var totalAffected = 0;

        foreach (var sc in containers)
        {
            await using var owned = sc;
            cancellationToken.ThrowIfCancellationRequested();
            totalAffected += await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        return totalAffected;
    }

    // ChunkList moved to BaseTableGateway.Core.cs

    private static List<TRowID> MaterializeDistinctIds(IEnumerable<TRowID> ids)
    {
        // Optimization: Fast path for common collection types to avoid double-enumeration or 
        // unnecessary allocations for single-element lists.
        if (ids is IReadOnlyCollection<TRowID> roc)
        {
            if (roc.Count == 0)
            {
                return new List<TRowID>(0);
            }
            if (roc.Count == 1)
            {
                var id = roc is IList<TRowID> list ? list[0] : roc.First();
                return new List<TRowID>(1) { id };
            }
        }

        var result = ids is ICollection<TRowID> coll
            ? new List<TRowID>(coll.Count)
            : new List<TRowID>();

        var comparer = EqualityComparer<TRowID>.Default;
        HashSet<TRowID>? seen = null;

        foreach (var id in ids)
        {
            if (result.Count == 0)
            {
                result.Add(id);
                continue;
            }

            // Threshold-based deduplication:
            // 1. Very small lists (< 16): Use linear search on result list (faster than HashSet overhead)
            // 2. Larger lists (>= 16): Switch to HashSet for O(1) lookups
            if (seen == null)
            {
                if (result.Count < 16)
                {
                    var found = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (comparer.Equals(result[i], id))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        result.Add(id);
                    }
                    continue;
                }

                // Transition to HashSet
                seen = new HashSet<TRowID>(result, comparer);
            }

            if (seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }


    /// <inheritdoc/>
    public async ValueTask<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (objectToRetrieve == null)
        {
            throw new ArgumentNullException(nameof(objectToRetrieve));
        }

        var ctx = context ?? _context;
        var list = new List<TEntity> { objectToRetrieve };
        await using var sc = BuildRetrieve(list, string.Empty, ctx);
        return await LoadSingleAsync(sc, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        var dialect = GetDialect(ctx);
        var templates = GetContainerTemplatesForDialect(dialect, ctx);
        using var container = templates.GetByIdTemplate!.Clone(ctx);
        container.SetParameterValue("p0", id);

        return await LoadSingleAsync(container, cancellationToken).ConfigureAwait(false);
    }

    // LoadSingleAsync, LoadListAsync, LoadStreamAsync moved to BaseTableGateway.Core.cs

    // GetCachedInsertableColumns moved to BaseTableGateway.Core.cs

    // CheckParameterLimit moved to BaseTableGateway.Core.cs

    private CompiledBinderFactory<TEntity>.Binder GetOrBuildInsertBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        return _insertBinders.GetOrAdd(dialect.DatabaseType, _ =>
            CompiledBinderFactory<TEntity>.CreateInsertBinder(template.InsertColumns, template.InsertParameterNames, dialect));
    }

    private CompiledBinderFactory<TEntity>.Binder GetOrBuildUpsertBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        return _upsertBinders.GetOrAdd(dialect.DatabaseType, _ =>
            CompiledBinderFactory<TEntity>.CreateInsertBinder(template.UpsertColumns, template.UpsertParameterNames, dialect));
    }

    private CompiledBinderFactory<TEntity>.UpdateBinder GetOrBuildUpdateBinder(ISqlDialect dialect, CachedSqlTemplates template)
    {
        return _updateBinders.GetOrAdd(dialect.DatabaseType, _ =>
            CompiledBinderFactory<TEntity>.CreateUpdateBinder(template.UpdateColumns, template.UpdateColumnWrappedNames, dialect));
    }

    // CheckParameterLimit moved to BaseTableGateway.Core.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs


    // moved to TableGateway.Update.cs

    // moved to TableGateway.Update.cs

    /// <inheritdoc/>
    public ValueTask<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        try
        {
            await using var sc = await BuildUpdateAsync(objectToUpdate, loadOriginal, ctx, cancellationToken).ConfigureAwait(false);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
            if (rowsAffected == 0 && _versionColumn != null)
            {
                throw new ConcurrencyConflictException(
                    $"Concurrency conflict on {typeof(TEntity).Name}: version mismatch or row deleted.",
                    ctx.Product);
            }

            return rowsAffected;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No changes detected for update."))
        {
            return 0;
        }
    }

    // moved to TableGateway.Upsert.cs

    // moved to TableGateway.Upsert.cs

    // moved to TableGateway.Upsert.cs

    // moved to TableGateway.Upsert.cs

    private (string sql, List<DbParameter> parameters) BuildUpdateByKey(TEntity updated,
        IReadOnlyList<IColumnInfo> keyCols, ISqlDialect dialect)
    {
        // Always attempt to set audit fields if they exist - validation will happen inside SetAuditFields
        if (_hasAuditColumns)
        {
            SetAuditFields(updated, true);
        }

        var counters = new ClauseCounters();
        var (setClause, parameters) = BuildSetClause(updated, null, dialect, ref counters);
        if (setClause.Length == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        // Append version increment if needed
        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            setClause += GetVersionIncrementClause(dialect);
        }

        var where = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        var sqlBuf = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            for (var i = 0; i < keyCols.Count; i++)
            {
                if (i > 0)
                {
                    where.Append(SqlFragments.And);
                }

                var key = keyCols[i];
                var v = key.MakeParameterValueFromField(updated);
                var name = counters.NextKey();
                var p = dialect.CreateDbParameter(name, key.DbType, v);
                parameters.Add(p);
                where.Append(dialect.WrapSimpleName(key.Name));
                where.Append(" = ");
                where.Append(dialect.MakeParameterName(name));
            }

            sqlBuf.Append("UPDATE ");
            sqlBuf.Append(BuildWrappedTableName(dialect));
            sqlBuf.Append(" SET ");
            sqlBuf.Append(setClause);
            sqlBuf.Append(" WHERE ");
            sqlBuf.Append(where.AsSpan());

            if (_versionColumn != null)
            {
                var vv = _versionColumn.MakeParameterValueFromField(updated);
                var name = counters.NextVer();
                var p = dialect.CreateDbParameter(name, _versionColumn.DbType, vv);
                parameters.Add(p);
                sqlBuf.Append(" AND ");
                sqlBuf.Append(dialect.WrapSimpleName(_versionColumn.Name));
                sqlBuf.Append(" = ");
                sqlBuf.Append(dialect.MakeParameterName(name));
            }

            return (sqlBuf.ToString(), parameters);
        }
        finally
        {
            where.Dispose();
            sqlBuf.Dispose();
        }
    }


    // moved to TableGateway.Update.cs

    // moved to TableGateway.Update.cs

    // moved to TableGateway.Update.cs

    // moved to TableGateway.Update.cs

    // moved to TableGateway.Upsert.cs


    // moved to TableGateway.Retrieve.cs

    /// <summary>
    /// Type-safe coercion for audit field values (handles string to Guid, etc.)
    /// </summary>
    // moved to TableGateway.Audit.cs

    // moved to TableGateway.Audit.cs

    private static bool IsDefaultId(object? value)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return true;
        }

        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return value as string == string.Empty;
        }

        if (underlying == typeof(Guid))
        {
            return value is Guid g && g == Guid.Empty;
        }

        if (Utils.IsZeroNumeric(value!))
        {
            return true;
        }

        if (value is TRowID typed)
        {
            return EqualityComparer<TRowID>.Default.Equals(typed, default!);
        }

        // Different runtime type than TRowID and not a zero/empty equivalent
        return false;
    }


    private static bool TryParseMajorVersion(string version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var match = Regex.Match(version, "(\\d+)");
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups[1].Value, out major);
    }


    private string GetSequenceName()
    {
        // Simple heuristic: [table_name]_seq
        return string.Concat(_tableInfo.Name, "_seq");
    }

    // BuildWrappedTableName and GetDialect moved to BaseTableGateway.Core.cs

    private static void ValidateRowIdType()
    {
        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        var isValid = underlying == typeof(string) || underlying == typeof(Guid);
        if (!isValid)
        {
            switch (Type.GetTypeCode(underlying))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    isValid = true;
                    break;
                default:
                    isValid = false;
                    break;
            }
        }

        if (!isValid)
        {
            throw new NotSupportedException(
                $"TRowID type '{type.FullName}' is not supported. Use string, Guid, or integer types.");
        }
    }
}
