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
    ITableGateway<TEntity, TRowID> where TEntity : class, new()
{
    private const string EmptyIdListMessage = "List of IDs cannot be empty.";
    private const string UpsertNoKeyMessage = "Upsert requires an Id or a composite primary key.";
    private const string UpsertNoWritableKeyMessage = "Upsert requires client-assigned Id or [PrimaryKey] attributes.";

    // Cache for compiled property setters
    private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>> _propertySetters = new();

    // Per-dialect templates are cached in _templatesByDialect


    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }

    static TableGateway()
    {
        ValidateRowIdType();
    }

    private readonly IAuditValueResolver? _auditValueResolver;
    private IDatabaseContext _context = null!;
    private ISqlDialect _dialect = null!;

    private IColumnInfo? _idColumn;

    private ITableInfo _tableInfo = null!;
    private IReadOnlyDictionary<string, IColumnInfo> _columnsByNameCI = null!;
    private bool _hasAuditColumns;

    private IColumnInfo? _versionColumn;

    private TypeCoercionOptions _coercionOptions = TypeCoercionOptions.Default;

    private readonly BoundedCache<string, IReadOnlyList<IColumnInfo>> _columnListCache = new(MaxCacheSize);

    private readonly BoundedCache<string, string> _queryCache = new(MaxCacheSize);

    private readonly BoundedCache<string, string[]> _whereParameterNames = new(MaxCacheSize);

    // Cache for wrapped table names per dialect (table name + schema never change, only dialect quoting varies)
    // Expected 3-5% reduction in SQL generation when the same TableGateway is used with different contexts/dialects
    private readonly ConcurrentDictionary<ISqlDialect, string> _wrappedTableNameCache = new();

    // Thread-safe cache for hybrid reader plans by recordset shape hash (long key avoids int-hash collisions)
    private readonly BoundedCache<long, HybridRecordsetPlan> _readerPlans = new(MaxReaderPlans);
    private const int MaxReaderPlans = 32;

    // Hybrid plan: Compiled expression for direct columns + delegates for coercion columns
    // Decision made once at plan-build time for maximum performance
    private sealed class HybridRecordsetPlan
    {
        // Compiled mapper for all direct-match columns (zero delegate overhead)
        public Func<ITrackedReader, TEntity>? CompiledMapper { get; }

        // Delegate-based plans for columns needing coercion (JSON, enums, type conversion)
        public CoercedColumnPlan[]? CoercionPlans { get; }

        public HybridRecordsetPlan(Func<ITrackedReader, TEntity>? compiledMapper, CoercedColumnPlan[]? coercionPlans)
        {
            CompiledMapper = compiledMapper;
            CoercionPlans = coercionPlans;
        }
    }

    // Optimized execution plan with pre-compiled delegates for blazing fast per-row execution
    // Abstract base - decision made at plan-build time, not per-row
    private abstract class ColumnPlan
    {
        public int Ordinal { get; }

        protected ColumnPlan(int ordinal)
        {
            Ordinal = ordinal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract void Apply(ITrackedReader reader, object target);
    }

    // Fast path: Direct type match, no coercion
    private sealed class DirectColumnPlan : ColumnPlan
    {
        private readonly Func<ITrackedReader, int, object?> _valueExtractor;
        private readonly Action<object, object?> _setter;

        public DirectColumnPlan(int ordinal, Func<ITrackedReader, int, object?> valueExtractor,
            Action<object, object?> setter)
            : base(ordinal)
        {
            _valueExtractor = valueExtractor;
            _setter = setter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Apply(ITrackedReader reader, object target)
        {
            if (!reader.IsDBNull(Ordinal))
            {
                try
                {
                    var value = _valueExtractor(reader, Ordinal);
                    _setter(target, value);
                }
                catch (Exception ex)
                {
                    if (ex is ArgumentException)
                    {
                        throw;
                    }

                    var columnName = reader.GetName(Ordinal);
                    throw new InvalidValueException(
                        $"Unable to set property from value that was stored in the database: {columnName} :{ex.Message}");
                }
            }
        }
    }

    // Slow path: Type coercion required (JSON, enum, type mismatch, etc.)
    private sealed class CoercedColumnPlan : ColumnPlan
    {
        private readonly Func<ITrackedReader, int, object?> _valueExtractor;
        private readonly Func<object?, object?> _coercer;
        private readonly Action<object, object?> _setter;
        private readonly Type _propertyType;

        public CoercedColumnPlan(int ordinal, Func<ITrackedReader, int, object?> valueExtractor,
            Func<object?, object?> coercer, Action<object, object?> setter, Type propertyType)
            : base(ordinal)
        {
            _valueExtractor = valueExtractor;
            _coercer = coercer;
            _setter = setter;
            _propertyType = propertyType;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Apply(ITrackedReader reader, object target)
        {
            if (!reader.IsDBNull(Ordinal))
            {
                try
                {
                    var raw = _valueExtractor(reader, Ordinal);
                    var value = _coercer(raw);
                    if (value == null && _propertyType.IsValueType &&
                        Nullable.GetUnderlyingType(_propertyType) == null)
                    {
                        return;
                    }

                    _setter(target, value);
                }
                catch (Exception ex)
                {
                    if (ex is ArgumentException)
                    {
                        throw;
                    }

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
    private readonly ConcurrentDictionary<SupportedDatabase, Lazy<CachedContainerTemplates>> _containersByDialect =
        new();


    // Unified constructor accepting optional audit resolver and optional logger (by name)
    public TableGateway(IDatabaseContext databaseContext,
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
        if (databaseContext is not ITypeMapAccessor accessor)
        {
            throw new InvalidOperationException(
                "IDatabaseContext must expose an internal TypeMapRegistry.");
        }

        _dialect = (databaseContext as ISqlDialectProvider)?.Dialect
                   ?? throw new InvalidOperationException(
                       "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
        _coercionOptions = _coercionOptions with { Provider = _dialect.DatabaseType };
        _tableInfo = accessor.TypeMapRegistry.GetTableInfo<TEntity>() ??
                     throw new InvalidOperationException($"Type {typeof(TEntity).FullName} is not a table.");
        _columnsByNameCI =
            _tableInfo.Columns.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
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

    /// <inheritdoc/>
    public string WrappedTableName { get; set; } = null!;

    /// <inheritdoc/>
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

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
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

    /// <summary>
    /// Replaces neutral tokens ({Q}/{q}/{S}) with dialect-specific quoting and parameter markers.
    /// </summary>
    public string ReplaceNeutralTokens(string sql)
    {
        if (sql == null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);

        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '{' && i + 2 < sql.Length && sql[i + 2] == '}')
            {
                var token = sql[i + 1];
                switch (token)
                {
                    case 'Q':
                        sb.Append(_dialect.QuotePrefix);
                        i += 2;
                        continue;
                    case 'q':
                        sb.Append(_dialect.QuoteSuffix);
                        i += 2;
                        continue;
                    case 'S':
                        sb.Append(_dialect.ParameterMarker);
                        i += 2;
                        continue;
                }
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


    /// <inheritdoc/>
    public Task<bool> CreateAsync(TEntity entity)
    {
        return CreateAsync(entity, _context);
    }

    /// <inheritdoc/>
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
                    var converted = TypeCoercionHelper.ConvertWithCache(generatedId, targetType);
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

    /// <inheritdoc/>
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
            var generatedId = await sc.ExecuteScalarWriteAsync<object>(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
            if (generatedId != null && generatedId != DBNull.Value)
            {
                var converted = TypeCoercionHelper.ConvertWithCache(generatedId, _idColumn.PropertyInfo.PropertyType);
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
    private const string OutputClausePlaceholder = "{output}";      // SQL Server: OUTPUT INSERTED.id (before VALUES)
    private const string ReturningClausePlaceholder = "{returning}"; // PostgreSQL/SQLite/etc: RETURNING id (after VALUES)

    /// <inheritdoc/>
    public ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null)
    {
        var (sc, _) = PrepareInsertContainer(entity, context, stripPlaceholders: true);
        return sc;
    }

    /// <summary>
    /// Prepares the SqlContainer with an INSERT statement.
    /// When stripPlaceholders is true, identity clause placeholders are removed.
    /// When false, placeholders remain for BuildCreateWithReturning to replace.
    /// </summary>
    private (ISqlContainer sc, ISqlDialect dialect) PrepareInsertContainer(TEntity entity, IDatabaseContext? context, bool stripPlaceholders)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);
        EnsureWritableIdHasValue(entity);

        if (_hasAuditColumns)
        {
            SetAuditFields(entity, false);
        }

        if (_versionColumn != null)
        {
            var current = _versionColumn.PropertyInfo.GetValue(entity);
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

        var template = GetTemplatesForDialect(dialect);

        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (");

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

            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapObjectName(column.Name));
        }

        // Insert OUTPUT placeholder (for SQL Server) between column list and VALUES
        sc.Query.Append(')')
            .Append(OutputClausePlaceholder)
            .Append(" VALUES (");

        for (var i = 0; i < template.InsertColumns.Count; i++)
        {
            var column = template.InsertColumns[i];
            var marker = dialect.MakeParameterName(template.InsertParameterNames[i]);
            if (column.IsJsonType)
            {
                marker = dialect.RenderJsonArgument(marker, column);
            }

            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(marker);
        }

        // Insert RETURNING placeholder (for PostgreSQL/SQLite/etc) after VALUES
        sc.Query.Append(')')
            .Append(ReturningClausePlaceholder);

        if (stripPlaceholders)
        {
            sc.Query.Replace(OutputClausePlaceholder, string.Empty);
            sc.Query.Replace(ReturningClausePlaceholder, string.Empty);
        }

        return (sc, dialect);
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

        if (withReturning && _idColumn != null && !_idColumn.IsIdIsWritable && dialect.SupportsInsertReturning)
        {
            var idWrapped = dialect.WrapObjectName(_idColumn.Name);
            var clause = dialect.RenderInsertReturningClause(idWrapped);

            if (dialect.DatabaseType == SupportedDatabase.SqlServer)
            {
                outputClause = clause;  // SQL Server: OUTPUT goes before VALUES
            }
            else
            {
                returningClause = clause;  // Others: RETURNING goes after VALUES
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
        var sql = GetCachedQuery(cacheKey,
            () => string.Format(GetTemplatesForDialect(dialect).DeleteSql, dialect.MakeParameterName(param)));
        sc.Query.Append(sql);
        return sc;
    }

    /// <inheritdoc/>
    public Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null)
    {
        return DeleteAsync(id, context, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAsync(TRowID id, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        var sc = BuildDelete(id, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        return RetrieveAsync(ids, context, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context,
        CancellationToken cancellationToken)
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
                var sc = BuildRetrieve(chunk, ctx);
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

    /// <inheritdoc/>
    public IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        return RetrieveStreamAsync(ids, context, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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
        var container = GetRetrieveContainer(list, ctx);

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
                var container = templates.GetByIdTemplate.Clone(ctx);

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
                var container = templates.GetByIdsTemplate.Clone(ctx);

                container.SetParameterValue("p0", list[0]);
                container.SetParameterValue("p1", list[1]);

                return container;
            }

            // Fall back to dynamic BuildRetrieve for larger lists
            return BuildRetrieve(list, ctx);
        }
        catch (Exception ex) when (ex.Message.Contains("Original record not found") || ex is AggregateException)
        {
            // Fall back to traditional method during template building or other issues
            return BuildRetrieve(list, ctx);
        }
    }

    /// <inheritdoc/>
    public Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)
    {
        return DeleteAsync(ids, context, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context,
        CancellationToken cancellationToken)
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

        var sc = ctx.CreateSqlContainer();
        var dialect = GetDialect(ctx);
        sc.Query.Append("DELETE FROM ").Append(BuildWrappedTableName(dialect));
        BuildWhere(sc.WrapObjectName(_idColumn.Name), list, sc);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }


    private static List<TRowID> MaterializeDistinctIds(IEnumerable<TRowID> ids)
    {
        // Fast path for single-element or empty collections (15-20% improvement for single-ID queries)
        if (ids is ICollection<TRowID> collection)
        {
            if (collection.Count == 0)
            {
                return new List<TRowID>();
            }

            if (collection.Count == 1)
            {
                // Most common pattern: RetrieveOneAsync(id) - no need for deduplication
                var singleResult = new List<TRowID>(1);
                using (var enumerator = collection.GetEnumerator())
                {
                    if (enumerator.MoveNext())
                    {
                        singleResult.Add(enumerator.Current);
                    }
                }
                return singleResult;
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


    /// <inheritdoc/>
    public Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null)
    {
        return RetrieveOneAsync(objectToRetrieve, context, CancellationToken.None);
    }

    /// <inheritdoc/>
    public Task<TEntity?> RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context,
        CancellationToken cancellationToken)
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

    /// <inheritdoc/>
    public Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null)
    {
        return RetrieveOneAsync(id, context, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context,
        CancellationToken cancellationToken)
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
        container.Query.Append(SqlFragments.Where);
        container.Query.Append(wrappedColumnName);
        container.Query.Append(SqlFragments.EqualsOp);
        container.Query.Append(paramName);

        // Add the parameter
        var parameter = container.CreateDbParameter(paramName, _idColumn.DbType, id);
        container.AddParameter(parameter);

        return await LoadSingleAsync(container, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<TEntity?> LoadSingleAsync(ISqlContainer sc)
    {
        return LoadSingleAsync(sc, CancellationToken.None);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public Task<List<TEntity>> LoadListAsync(ISqlContainer sc)
    {
        return LoadListAsync(sc, CancellationToken.None);
    }

    /// <inheritdoc/>
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
        HybridRecordsetPlan? plan = null;

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

    /// <inheritdoc/>
    public async IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc)
    {
        if (sc == null)
        {
            throw new ArgumentNullException(nameof(sc));
        }

        await using var reader =
            await sc.ExecuteReaderAsync(CommandType.Text, CancellationToken.None).ConfigureAwait(false);

        // Reader optimization: hoist plan building outside the loop
        // Build plan once based on first row's schema, then reuse for all rows
        // This avoids hash calculation and GetName/GetFieldType calls on every row
        HybridRecordsetPlan? plan = null;

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            // Build plan on first row
            if (plan == null)
            {
                plan = GetOrBuildRecordsetPlan(reader);
            }

            var obj = MapReaderToObjectWithPlan(reader, plan);
            if (obj != null)
            {
                yield return obj;
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (sc == null)
        {
            throw new ArgumentNullException(nameof(sc));
        }

        await using var reader = await sc.ExecuteReaderAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);

        // Reader optimization: hoist plan building outside the loop
        // Build plan once based on first row's schema, then reuse for all rows
        // This avoids hash calculation and GetName/GetFieldType calls on every row
        HybridRecordsetPlan? plan = null;

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
                yield return obj;
            }
        }
    }

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    internal IReadOnlyList<IColumnInfo> GetCachedInsertableColumns()
    {
        if (_columnListCache.TryGet("Insertable", out var cached))
        {
            return cached;
        }

        // Avoid LINQ allocations in hot path
        var insertable = new List<IColumnInfo>(_tableInfo.OrderedColumns.Count);
        foreach (var c in _tableInfo.OrderedColumns)
        {
            if (!c.IsNonInsertable && (!c.IsId || c.IsIdIsWritable))
            {
                insertable.Add(c);
            }
        }

        return _columnListCache.GetOrAdd("Insertable", _ => insertable);
    }

    internal IReadOnlyList<IColumnInfo> GetCachedUpdatableColumns()
    {
        if (_columnListCache.TryGet("Updatable", out var cached))
        {
            return cached;
        }

        // Avoid LINQ allocations in hot path (5-8% CPU savings)
        var updatable = new List<IColumnInfo>(_tableInfo.OrderedColumns.Count);
        foreach (var c in _tableInfo.OrderedColumns)
        {
            if (!(c.IsId || c.IsVersion || c.IsNonUpdateable || c.IsCreatedBy || c.IsCreatedOn))
            {
                updatable.Add(c);
            }
        }

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

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs

    // moved to TableGateway.Retrieve.cs


    // moved to TableGateway.Update.cs

    // moved to TableGateway.Update.cs

    /// <inheritdoc/>
    public Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx, CancellationToken.None);
    }

    /// <inheritdoc/>
    public Task<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        var ctx = context ?? _context;
        return UpdateAsync(objectToUpdate, _versionColumn != null, ctx, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null)
    {
        return UpdateAsync(objectToUpdate, loadOriginal, context, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context,
        CancellationToken cancellationToken)
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

    // moved to TableGateway.Upsert.cs

    // moved to TableGateway.Upsert.cs

    // moved to TableGateway.Upsert.cs

    // moved to TableGateway.Upsert.cs

// moved to TableGateway.Upsert.cs
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

        throw new NotSupportedException(UpsertNoWritableKeyMessage);
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

        // Append version increment if needed
        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            setClause += GetVersionIncrementClause(dialect);
        }

        var where = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
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
            where.Append($"{dialect.WrapObjectName(key.Name)} = {dialect.MakeParameterName(p)}");
        }

        var sql = $"UPDATE {BuildWrappedTableName(dialect)} SET {setClause} WHERE {where.ToString()}";
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
        // Cache wrapped table names per dialect since table/schema never change
        return _wrappedTableNameCache.GetOrAdd(dialect, d =>
        {
            if (string.IsNullOrWhiteSpace(_tableInfo.Schema))
            {
                return d.WrapObjectName(_tableInfo.Name);
            }

            var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
            sb.Append(d.WrapObjectName(_tableInfo.Schema));
            sb.Append(d.CompositeIdentifierSeparator);
            sb.Append(d.WrapObjectName(_tableInfo.Name));
            return sb.ToString();
        });
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
