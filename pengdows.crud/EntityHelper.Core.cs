#region

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID> :
    IEntityHelper<TEntity, TRowID> where TEntity : class, new()
{
    // Cache for compiled property setters
    private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>> _propertySetters = new();

    // Per-dialect templates are cached in _templatesByDialect


    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }

    static EntityHelper()
    {
        ValidateRowIdType();
    }

    private readonly IAuditValueResolver? _auditValueResolver;
    private IDatabaseContext _context = null!;
    private ISqlDialect _dialect = null!;

    private IColumnInfo? _idColumn;

    // private readonly IServiceProvider _serviceProvider;
    private ITableInfo _tableInfo = null!;
    private IReadOnlyDictionary<string, IColumnInfo> _columnsByNameCI = null!;
    private bool _hasAuditColumns;

    private IColumnInfo? _versionColumn;

    private TypeCoercionOptions _coercionOptions = TypeCoercionOptions.Default;

    private readonly BoundedCache<string, IReadOnlyList<IColumnInfo>> _columnListCache = new(MaxCacheSize);

    private readonly BoundedCache<string, string> _queryCache = new(MaxCacheSize);

    private readonly BoundedCache<string, string[]> _whereParameterNames = new(MaxCacheSize);

    // Thread-safe cache for reader plans by recordset shape hash
    private readonly BoundedCache<int, ColumnPlan[]> _readerPlans = new(MaxReaderPlans);
    private const int MaxReaderPlans = 32;

    // Optimized execution plan with pre-compiled delegates for blazing fast per-row execution
    private sealed class ColumnPlan
    {
        public int Ordinal { get; }
        public Func<ITrackedReader, int, object?> ValueExtractor { get; }  // Fast typed value extraction
        public Func<object?, object?>? Coercer { get; }                     // Pre-compiled type coercion (null if not needed)
        public Action<object, object?> Setter { get; }                     // Pre-compiled property setter
        private Type PropertyType { get; }

        public ColumnPlan(int ordinal, Func<ITrackedReader, int, object?> valueExtractor,
                         Func<object?, object?>? coercer, Action<object, object?> setter,
                         Type propertyType)
        {
            Ordinal = ordinal;
            ValueExtractor = valueExtractor;
            Coercer = coercer;
            Setter = setter;
            PropertyType = propertyType;
        }

        // Optimized execution: get → coerce (if needed) → set (2-3 fast operations)
        public void Apply(ITrackedReader reader, object target)
        {
            if (!reader.IsDBNull(Ordinal))
            {
                try
                {
                    var raw = ValueExtractor(reader, Ordinal);
                    var value = Coercer != null ? Coercer(raw) : raw;  // Skip coercion if types match
                    if (value == null && PropertyType.IsValueType && Nullable.GetUnderlyingType(PropertyType) == null)
                    {
                        return;
                    }

                    Setter(target, value);
                }
                catch (Exception ex)
                {
                    // Let certain exceptions bubble up unchanged (e.g., ArgumentException for enum parsing in Throw mode)
                    if (ex is ArgumentException)
                        throw;

                    var columnName = reader.GetName(Ordinal);
                    throw new InvalidValueException(
                        $"Unable to set property from value that was stored in the database: {columnName} :{ex.Message}");
                }
            }
        }
    }

    // SQL templates cached per dialect to support context overrides
    private readonly ConcurrentDictionary<SupportedDatabase, Lazy<CachedSqlTemplates>> _templatesByDialect = new();
    
    // Pre-built SqlContainer cache for common operations (GetById, GetByIds, etc.)
    private readonly ConcurrentDictionary<SupportedDatabase, Lazy<CachedContainerTemplates>> _containersByDialect = new();



    // Unified constructor accepting optional audit resolver and optional logger (by name)
    public EntityHelper(IDatabaseContext databaseContext,
        IAuditValueResolver? auditValueResolver = null,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw,
        ILogger? logger = null)
    {
        _auditValueResolver = auditValueResolver;
        if (logger != null)
        {
            Logger = logger;
        }

        Initialize(databaseContext, enumParseBehavior);
        // Templates are now built directly per dialect
    }

    private void Initialize(IDatabaseContext databaseContext, EnumParseFailureMode enumParseBehavior)
    {
        _context = databaseContext;
        _dialect = (databaseContext as ISqlDialectProvider)?.Dialect
            ?? throw new InvalidOperationException(
                "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
        _coercionOptions = _coercionOptions with { Provider = _dialect.DatabaseType };
        _tableInfo = _context.TypeMapRegistry.GetTableInfo<TEntity>() ??
                     throw new InvalidOperationException($"Type {typeof(TEntity).FullName} is not a table.");
        _columnsByNameCI = _tableInfo.Columns.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        _hasAuditColumns = _tableInfo.HasAuditColumns;

        if (_hasAuditColumns && _auditValueResolver is null)
        {
            Logger.LogWarning(
                "Entity {EntityType} declares audit columns but no IAuditValueResolver is provided; audit fields may not be populated.",
                typeof(TEntity).FullName
            );
        }

        WrappedTableName = (!string.IsNullOrEmpty(_tableInfo.Schema)
                               ? WrapObjectName(_tableInfo.Schema) +
                                 _dialect.CompositeIdentifierSeparator
                               : "")
                           + WrapObjectName(_tableInfo.Name);

        _idColumn = _tableInfo.Columns.Values.FirstOrDefault(itm => itm.IsId);
        _versionColumn = _tableInfo.Columns.Values.FirstOrDefault(itm => itm.IsVersion);

        // Note: Validation for missing audit resolver with user fields is now handled
        // in SetAuditFields method where it throws InvalidOperationException

        EnumParseBehavior = enumParseBehavior;
    }

    public string WrappedTableName { get; set; } = null!;

    public EnumParseFailureMode EnumParseBehavior { get; set; }

    public string QuotePrefix => _dialect.QuotePrefix;

    public string QuoteSuffix => _dialect.QuoteSuffix;

    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;

    public string MakeParameterName(DbParameter p)
    {
        return _dialect.MakeParameterName(p);
    }

    /// <summary>
    /// Legacy dialect token replacement method. Use neutral token system instead.
    /// </summary>
    [Obsolete("Use neutral token system with {Q}/{q}/{S} tokens instead")]
    public string ReplaceDialectTokens(string sql, string quotePrefix, string quoteSuffix, string parameterMarker)
    {
        if (sql == null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var dialectPrefix = _dialect.QuotePrefix;
        var dialectSuffix = _dialect.QuoteSuffix;
        var dialectMarker = _dialect.ParameterMarker;

        var sb = new StringBuilder(sql.Length);
        var inQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            if (!inQuote && sql.AsSpan(i).StartsWith(dialectPrefix))
            {
                sb.Append(quotePrefix);
                i += dialectPrefix.Length - 1;
                inQuote = true;
                continue;
            }

            if (inQuote && sql.AsSpan(i).StartsWith(dialectSuffix))
            {
                sb.Append(quoteSuffix);
                i += dialectSuffix.Length - 1;
                inQuote = false;
                continue;
            }

            if (sql.AsSpan(i).StartsWith(dialectMarker))
            {
                sb.Append(parameterMarker);
                i += dialectMarker.Length - 1;
                continue;
            }

            sb.Append(sql[i]);
        }

        return sb.ToString();
    }

    private string GetCachedQuery(string key, Func<string> factory)
    {
        if (_queryCache.TryGet(key, out var sql))
        {
            return sql;
        }

        return _queryCache.GetOrAdd(key, _ => factory());
    }

    
    public Task<bool> CreateAsync(TEntity entity)
    {
        return CreateAsync(entity, _context);
    }

    public async Task<bool> CreateAsync(TEntity entity, IDatabaseContext context)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        
        // Use RETURNING clause if supported to avoid race condition
        if (_idColumn != null && !_idColumn.IsIdIsWritable && dialect.SupportsInsertReturning)
        {
            var sc = BuildCreateWithReturning(entity, true, ctx);
            var generatedId = await sc.ExecuteScalarWriteAsync<object>();
            
            if (generatedId != null && generatedId != DBNull.Value)
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
                }
                else
                {
                    var converted = Convert.ChangeType(generatedId, targetType, CultureInfo.InvariantCulture);
                    _idColumn.PropertyInfo.SetValue(entity, converted);
                }
            }
            else
            {
                // Fallback: some providers/tests return null from INSERT ... RETURNING under fakeDb
                // Attempt to populate via provider-specific last-insert-id query
                await PopulateGeneratedIdAsync(entity, ctx).ConfigureAwait(false);
            }
            // Insert succeeded if we reached here; ID may be null, which is acceptable
            return true;
        }
        else
        {
            // Fallback to old behavior for databases that don't support RETURNING
            var sc = BuildCreate(entity, ctx);
            var rowsAffected = await sc.ExecuteNonQueryAsync();

            // For non-writable ID columns, retrieve the generated ID and populate it on the entity
            if (rowsAffected == 1 && _idColumn != null && !_idColumn.IsIdIsWritable)
            {
                await PopulateGeneratedIdAsync(entity, ctx);
            }

            return rowsAffected == 1;
        }
    }

    public async Task<bool> CreateAsync(TEntity entity, IDatabaseContext context, CancellationToken cancellationToken)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        if (_idColumn != null && !_idColumn.IsIdIsWritable && dialect.SupportsInsertReturning)
        {
            var sc = BuildCreateWithReturning(entity, true, ctx);
            var generatedId = await sc.ExecuteScalarWriteAsync<object>(CommandType.Text, cancellationToken).ConfigureAwait(false);
            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = Convert.ChangeType(generatedId, _idColumn.PropertyInfo.PropertyType, CultureInfo.InvariantCulture);
                _idColumn.PropertyInfo.SetValue(entity, converted);
                return true;
            }
            // Fallback path: try last-insert-id
            await PopulateGeneratedIdAsync(entity, ctx).ConfigureAwait(false);
            return true;
        }
        else
        {
            var sc = BuildCreate(entity, ctx);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
            if (rowsAffected == 1 && _idColumn != null && !_idColumn.IsIdIsWritable)
            {
                await PopulateGeneratedIdAsync(entity, ctx).ConfigureAwait(false);
            }
            return rowsAffected == 1;
        }
    }

    private async Task PopulateGeneratedIdAsync(TEntity entity, IDatabaseContext context)
    {
        if (_idColumn == null)
        {
            return;
        }

        // Get the database-specific query for retrieving the last inserted ID
        var ctx = context ?? _context;
        var provider = ctx as ISqlDialectProvider
                       ?? throw new InvalidOperationException(
                           "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");

        string lastIdQuery;
        try
        {
            lastIdQuery = provider.Dialect.GetLastInsertedIdQuery();
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

        var sc = ctx.CreateSqlContainer(lastIdQuery);
        var generatedId = await sc.ExecuteScalarAsync<object>();

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
                        var convertedId = Convert.ChangeType(generatedId, targetType, CultureInfo.InvariantCulture);
                        _idColumn.PropertyInfo.SetValue(entity, convertedId);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to convert generated ID '{generatedId}' to type {_idColumn.PropertyInfo.PropertyType.Name}: {ex.Message}", ex);
                }
            }
    }


    public ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);
        EnsureWritableIdHasValue(entity);
        // Always attempt to set audit fields if they exist - validation will happen inside SetAuditFields
        if (_hasAuditColumns)
        {
            SetAuditFields(entity, false);
        }

        if (_versionColumn != null)
        {
            var current = _versionColumn.PropertyInfo.GetValue(entity);
            if (current == null || Utils.IsZeroNumeric(current))
            {
                var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ?? _versionColumn.PropertyInfo.PropertyType;
                if (Utils.IsZeroNumeric(Convert.ChangeType(0, target)))
                {
                    var one = Convert.ChangeType(1, target);
                    _versionColumn.PropertyInfo.SetValue(entity, one);
                }
            }
        }

        var template = GetTemplatesForDialect(dialect);
        var columns = new List<string>();
        var placeholders = new List<string>();

        for (var i = 0; i < template.InsertColumns.Count; i++)
        {
            var column = template.InsertColumns[i];
            var value = column.MakeParameterValueFromField(entity);

            var paramName = template.InsertParameterNames[i];
            var param = dialect.CreateDbParameter(paramName, column.DbType, value);
            if (column.IsJsonType)
            {
                dialect.TryMarkJsonParameter(param, column);
            }
            sc.AddParameter(param);
            columns.Add(dialect.WrapObjectName(column.Name));
            var marker = dialect.MakeParameterName(param);
            if (column.IsJsonType)
            {
                marker = dialect.RenderJsonArgument(marker, column);
            }

            placeholders.Add(marker);
        }

        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", placeholders))
            .Append(")");

        return sc;
    }

    private void EnsureWritableIdHasValue(TEntity entity)
    {
        if (_idColumn == null || !_idColumn.IsIdIsWritable)
        {
            return;
        }

        var property = _idColumn.PropertyInfo;
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var currentValue = property.GetValue(entity);

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
    /// Builds an INSERT statement with optional RETURNING clause for identity capture.
    /// </summary>
    /// <param name="entity">Entity to insert</param>
    /// <param name="withReturning">Whether to include RETURNING clause for identity</param>
    /// <param name="context">Database context</param>
    /// <returns>SQL container with INSERT statement</returns>
    public ISqlContainer BuildCreateWithReturning(TEntity entity, bool withReturning, IDatabaseContext? context = null)
    {
        var sc = BuildCreate(entity, context);
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        
        // Add RETURNING clause if requested and supported
        if (withReturning && _idColumn != null && !_idColumn.IsIdIsWritable && dialect.SupportsInsertReturning)
        {
            var idWrapped = dialect.WrapObjectName(_idColumn.Name);
            var returningClause = dialect.RenderInsertReturningClause(idWrapped);
            sc.Query.Append(returningClause);
        }
        
        return sc;
    }

    // moved to EntityHelper.Retrieve.cs

    public ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);

        if (_idColumn == null)
        {
            throw new InvalidOperationException($"row identity column for table {WrappedTableName} not found");
        }

        var counters = new ClauseCounters();
        var name = counters.NextKey();
        var param = dialect.CreateDbParameter(name, _idColumn.DbType, id);
        sc.AddParameter(param);

        var baseKey = "DeleteById";
        var cacheKey = ctx.Product == _context.Product ? baseKey : $"{baseKey}:{ctx.Product}";
        var sql = GetCachedQuery(cacheKey, () => string.Format(GetTemplatesForDialect(dialect).DeleteSql, dialect.MakeParameterName(param)));
        sc.Query.Append(sql);
        return sc;
    }

    public Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null)
    {
        return DeleteAsync(id, context, CancellationToken.None);
    }

    public async Task<int> DeleteAsync(TRowID id, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        var sc = BuildDelete(id, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    public Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        return RetrieveAsync(ids, context, CancellationToken.None);
    }

    public async Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            throw new ArgumentException("List of IDs cannot be empty.", nameof(ids));
        }

        var ctx = context ?? _context;
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
                using var container = templates.GetByIdTemplate.Clone(ctx);
                
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
                using var container = templates.GetByIdsTemplate.Clone(ctx);

                container.SetParameterValue("p0", list[0]);
                container.SetParameterValue("p1", list[1]);

                return await LoadListAsync(container, cancellationToken).ConfigureAwait(false);
            }

            if (dialect.SupportsSetValuedParameters)
            {
                // Prefer dynamic build for array-capable dialects to avoid provider type mismatches
                var sc = BuildRetrieve(list, ctx);
                return await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Fall back to dynamic BuildRetrieve for larger lists on non-array dialects
                var sc = BuildRetrieve(list, ctx);
                return await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex.Message.Contains("Original record not found") || ex is AggregateException)
        {
            // Fall back to traditional method during template building or other issues
            var sc = BuildRetrieve(list, ctx);
            return await LoadListAsync(sc, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        return DeleteAsync(ids, context, CancellationToken.None);
    }

    public async Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        if (ids == null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        var list = MaterializeDistinctIds(ids);
        if (list.Count == 0)
        {
            throw new ArgumentException("List of IDs cannot be empty.", nameof(ids));
        }

        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);
        sc.Query.Append("DELETE FROM ").Append(BuildWrappedTableName(dialect));
        BuildWhere(sc.WrapObjectName(_idColumn.Name), list, sc);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }


    private static List<TRowID> MaterializeDistinctIds(IEnumerable<TRowID> ids)
    {
        var result = ids is ICollection<TRowID> collection
            ? new List<TRowID>(collection.Count)
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

            if (seen is null)
            {
                var duplicate = false;
                foreach (var existing in result)
                {
                    if (comparer.Equals(existing, id))
                    {
                        seen = new HashSet<TRowID>(result, comparer);
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                result.Add(id);
                continue;
            }

            if (seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }


    public Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null)
    {
        return RetrieveOneAsync(objectToRetrieve, context, CancellationToken.None);
    }

    public Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        if (objectToRetrieve == null)
        {
            throw new ArgumentNullException(nameof(objectToRetrieve));
        }
        var ctx = context ?? _context;
        var list = new List<TEntity> { objectToRetrieve };
        var sc = BuildRetrieve(list, string.Empty, ctx);
        return LoadSingleAsync(sc, cancellationToken);
    }

    public Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null)
    {
        return RetrieveOneAsync(id, context, CancellationToken.None);
    }

    public async Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }

        // Fast path: generate simple equality SQL directly instead of using expensive templates
        using var container = BuildBaseRetrieve("", ctx);

        // Add simple WHERE clause: column = @p0
        var wrappedColumnName = container.WrapObjectName(_idColumn.Name);
        var paramName = container.MakeParameterName("p0");
        container.Query.Append(" WHERE ");
        container.Query.Append(wrappedColumnName);
        container.Query.Append(" = ");
        container.Query.Append(paramName);

        // Add the parameter
        var parameter = container.CreateDbParameter(paramName, _idColumn.DbType, id);
        container.AddParameter(parameter);

        return await LoadSingleAsync(container, cancellationToken).ConfigureAwait(false);
    }

    public Task<TEntity?> LoadSingleAsync(ISqlContainer sc)
    {
        return LoadSingleAsync(sc, CancellationToken.None);
    }

    public async Task<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken cancellationToken)
    {
        if (sc == null)
        {
            throw new ArgumentNullException(nameof(sc));
        }
        // Hint provider/ADO.NET to expect a single row for minimal overhead
        await using var reader = await sc.ExecuteReaderSingleRowAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Reader optimization: build plan once before processing row
            var plan = GetOrBuildRecordsetPlan(reader);
            return MapReaderToObjectWithPlan(reader, plan);
        }

        return null;
    }

    public Task<List<TEntity>> LoadListAsync(ISqlContainer sc)
    {
        return LoadListAsync(sc, CancellationToken.None);
    }

    public async Task<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken cancellationToken)
    {
        if (sc == null)
        {
            throw new ArgumentNullException(nameof(sc));
        }
        var list = new List<TEntity>();

        await using var reader = await sc.ExecuteReaderAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);

        // Reader optimization: hoist plan building outside the loop
        // Build plan once based on first row's schema, then reuse for all rows
        // This avoids hash calculation and GetName/GetFieldType calls on every row
        ColumnPlan[]? plan = null;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Build plan on first row
            if (plan == null)
            {
                plan = GetOrBuildRecordsetPlan(reader);
            }

            var obj = MapReaderToObjectWithPlan(reader, plan);
            if (obj != null)
            {
                list.Add(obj);
            }
        }

        return list;
    }

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    private IReadOnlyList<IColumnInfo> GetCachedInsertableColumns()
    {
        if (_columnListCache.TryGet("Insertable", out var cached))
        {
            return cached;
        }

        var insertable = _tableInfo.OrderedColumns
            .Where(c => !c.IsNonInsertable && (!c.IsId || c.IsIdIsWritable))
            .ToList();

        return _columnListCache.GetOrAdd("Insertable", _ => insertable);
    }

    private IReadOnlyList<IColumnInfo> GetCachedUpdatableColumns()
    {
        if (_columnListCache.TryGet("Updatable", out var cached))
        {
            return cached;
        }

        var updatable = _tableInfo.OrderedColumns
            .Where(c => !(c.IsId || c.IsVersion || c.IsNonUpdateable || c.IsCreatedBy || c.IsCreatedOn))
            .ToList();

        return _columnListCache.GetOrAdd("Updatable", _ => updatable);
    }

    private void CheckParameterLimit(ISqlContainer sc, int? toAdd)
    {
        var count = sc.ParameterCount + (toAdd ?? 0);
        if (count > _context.MaxParameterLimit)
        {
            // For large batches consider chunking inputs; this method fails fast when exceeding limits.
            throw new TooManyParametersException("Too many parameters", _context.MaxParameterLimit);
        }
    }

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs

    // moved to EntityHelper.Retrieve.cs


    // moved to EntityHelper.Update.cs

    // moved to EntityHelper.Update.cs

    public Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx, CancellationToken.None);
    }

    public Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx, cancellationToken);
    }

    public Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null)
    {
        return UpdateAsync(objectToUpdate, loadOriginal, context, CancellationToken.None);
    }

    public async Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        try
        {
            var sc = await BuildUpdateAsync(objectToUpdate, loadOriginal, ctx, cancellationToken).ConfigureAwait(false);
            return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No changes detected for update."))
        {
            return 0;
        }
    }

    // moved to EntityHelper.Upsert.cs

    // moved to EntityHelper.Upsert.cs

    // moved to EntityHelper.Upsert.cs

    // moved to EntityHelper.Upsert.cs

// moved to EntityHelper.Upsert.cs
private IReadOnlyList<IColumnInfo> ResolveUpsertKey_MOVED()
    {
        if (_idColumn != null && _idColumn.IsIdIsWritable)
        {
            return new List<IColumnInfo> { _idColumn! };
        }

        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count > 0)
        {
            return keys;
        }

        throw new NotSupportedException("Upsert requires client-assigned Id or [PrimaryKey] attributes.");
    }

    private (string sql, List<DbParameter> parameters) BuildUpdateByKey(TEntity updated,
        IReadOnlyList<IColumnInfo> keyCols, ISqlDialect dialect)
    {
        // Always attempt to set audit fields if they exist - validation will happen inside SetAuditFields
        if (_hasAuditColumns)
        {
            SetAuditFields(updated, true);
        }
        var counters = new ClauseCounters();
        var (setClause, parameters) = BuildSetClause(updated, null, dialect, counters);
        if (setClause.Length == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            IncrementVersion(setClause, dialect);
        }

        var where = new StringBuilder();
        for (var i = 0; i < keyCols.Count; i++)
        {
            if (i > 0)
            {
                where.Append(" AND ");
            }

            var key = keyCols[i];
            var v = key.MakeParameterValueFromField(updated);
            var name = counters.NextKey();
            var p = dialect.CreateDbParameter(name, key.DbType, v);
            parameters.Add(p);
            where.Append($"{dialect.WrapObjectName(key.Name)} = {dialect.MakeParameterName(p)}");
        }

        var sql = $"UPDATE {BuildWrappedTableName(dialect)} SET {setClause} WHERE {where}";
        if (_versionColumn != null)
        {
            var vv = _versionColumn.MakeParameterValueFromField(updated);
            var name = counters.NextVer();
            var p = dialect.CreateDbParameter(name, _versionColumn.DbType, vv);
            parameters.Add(p);
            sql += $" AND {dialect.WrapObjectName(_versionColumn.Name)} = {dialect.MakeParameterName(p)}";
        }

        return (sql, parameters);
    }


 

    // moved to EntityHelper.Update.cs

    // moved to EntityHelper.Update.cs

    // moved to EntityHelper.Update.cs

    // moved to EntityHelper.Update.cs

    // moved to EntityHelper.Upsert.cs

 

 

 




    // moved to EntityHelper.Retrieve.cs

    /// <summary>
    /// Type-safe coercion for audit field values (handles string to Guid, etc.)
    /// </summary>
    // moved to EntityHelper.Audit.cs

    // moved to EntityHelper.Audit.cs

    private string WrapObjectName(string objectName)
    {
        return _dialect.WrapObjectName(objectName);
    }

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


    private string BuildWrappedTableName(ISqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(_tableInfo.Schema))
        {
            return dialect.WrapObjectName(_tableInfo.Name);
        }

        var sb = new StringBuilder();
        sb.Append(dialect.WrapObjectName(_tableInfo.Schema));
        sb.Append(dialect.CompositeIdentifierSeparator);
        sb.Append(dialect.WrapObjectName(_tableInfo.Name));
        return sb.ToString();
    }

    private static ISqlDialect GetDialect(IDatabaseContext ctx)
    {
        return (ctx as ISqlDialectProvider)?.Dialect
               ?? throw new InvalidOperationException(
                   "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
    }

    private static void ValidateRowIdType()
    {
        var type = typeof(TRowID);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        bool isValid = underlying == typeof(string) || underlying == typeof(Guid);
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
