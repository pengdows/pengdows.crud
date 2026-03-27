// =============================================================================
// FILE: BaseTableGateway.Core.cs
// PURPOSE: Abstract base class for all table gateway variants.
//          Contains identity-neutral shared code: fields, initialization,
//          load methods, and helpers used by both PrimaryKeyTableGateway<TEntity>
//          and TableGateway<TEntity, TRowID>.
//
// AI SUMMARY:
// - Shared fields: context, dialect, tableInfo, caches, audit setters.
// - protected constructor: accepts IDatabaseContext + optional audit resolver.
// - Initialize(): sets up all shared state (no [Id]-specific logic).
// - LoadSingleAsync, LoadListAsync, LoadStreamAsync: execute ISqlContainer + map rows.
// - BuildWrappedTableName, GetDialect: shared SQL helpers.
// - GetCachedInsertableColumns, ChunkList, CheckParameterLimit: shared batch helpers.
// =============================================================================

#region

using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Abstract identity-neutral base for all table gateway variants.
/// Contains shared fields, initialization, load methods, and SQL helpers.
/// </summary>
/// <typeparam name="TEntity">Entity type mapped to the table. Must have a parameterless constructor.</typeparam>
public abstract partial class BaseTableGateway<TEntity> : ITableGatewayInfrastructure<TEntity>
    where TEntity : class, new()
{
    // =========================================================================
    // Static fields
    // =========================================================================

    // Cache for compiled property setters (shared across all gateway instances for same TEntity)
    private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>> _propertySetters = new();

    private static volatile ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get => _logger;
        internal set => _logger = value ?? NullLogger.Instance;
    }

    // =========================================================================
    // Cache constants
    // =========================================================================

    private const int DefaultReaderPlanCapacity = 32;

    /// <summary>
    /// Maximum number of entries in bounded caches before LRU eviction.
    /// </summary>
    private const int MaxCacheSize = 100;

    // =========================================================================
    // Shared instance fields
    // =========================================================================

    protected readonly IAuditValueResolver? _auditValueResolver;
    protected IDatabaseContext _context = null!;
    private ISqlDialect _dialect = null!;

    internal ITableInfo _tableInfo = null!;
    private IReadOnlyDictionary<string, IColumnInfo> _columnsByNameCI = null!;

    protected bool _hasAuditColumns;
    internal IColumnInfo? _versionColumn;
    private TypeCoercionOptions _coercionOptions = TypeCoercionOptions.Default;

    // Cached compiled setters for audit fields — initialized once in Initialize()
    private Action<object, object?>? _auditLastUpdatedOnSetter;
    private Action<object, object?>? _auditLastUpdatedBySetter;
    private Action<object, object?>? _auditCreatedOnSetter;
    private Action<object, object?>? _auditCreatedBySetter;

    // =========================================================================
    // Cache instance fields
    // =========================================================================

    internal readonly BoundedCache<string, IReadOnlyList<IColumnInfo>> _columnListCache = new(MaxCacheSize);

    // Keyed by dialect (SupportedDatabase) so different dialects never share SQL strings
    private readonly ConcurrentDictionary<SupportedDatabase, BoundedCache<string, string>> _queryCache = new();

    private readonly ConcurrentDictionary<SupportedDatabase, BoundedCache<string, string[]>> _whereParameterNames =
        new();

    // Cache for wrapped table names per dialect
    private readonly ConcurrentDictionary<ISqlDialect, string> _wrappedTableNameCache = new();

    // Thread-safe cache for hybrid reader plans by recordset shape hash
    private BoundedCache<long, HybridRecordsetPlan> _readerPlans =
        new(DefaultReaderPlanCapacity);

    // =========================================================================
    // Properties
    // =========================================================================

    /// <inheritdoc/>
    public string WrappedTableName { get; init; } = null!;

    /// <inheritdoc/>
    public EnumParseFailureMode EnumParseBehavior { get; init; }

    protected IDatabaseContext Context => _context;

    // =========================================================================
    // Nested types
    // =========================================================================

    // Hybrid plan: Monolithic compiled expression that handles all columns.
    // Compiled once per schema shape for maximum performance.
    private sealed class HybridRecordsetPlan
    {
        public Func<ITrackedReader, TEntity> CompiledMapper { get; }

        public HybridRecordsetPlan(Func<ITrackedReader, TEntity> compiledMapper)
        {
            CompiledMapper = compiledMapper ?? throw new ArgumentNullException(nameof(compiledMapper));
        }
    }

    // =========================================================================
    // Constructor
    // =========================================================================

    /// <summary>
    /// Base constructor: stores audit resolver, optionally updates static logger,
    /// then calls <see cref="Initialize"/>.
    /// </summary>
    protected BaseTableGateway(
        IDatabaseContext databaseContext,
        IAuditValueResolver? auditValueResolver = null,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw,
        ILogger? logger = null)
    {
        _auditValueResolver = auditValueResolver;
        if (logger != null)
        {
            Logger = logger;
        }

        EnumParseBehavior = enumParseBehavior;
        _context = databaseContext;
        if (databaseContext is not ITypeMapAccessor accessor)
        {
            throw new InvalidOperationException(
                "IDatabaseContext must expose an internal TypeMapRegistry.");
        }

        _dialect = databaseContext.GetDialect();
        _coercionOptions = _coercionOptions with { Provider = _dialect.DatabaseType };
        _readerPlans = new BoundedCache<long, HybridRecordsetPlan>(ResolveReaderPlanCacheSize(databaseContext));

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

        WrappedTableName = (!string.IsNullOrEmpty(_tableInfo.Schema) && _dialect.SupportsNamespaces
                               ? _dialect.WrapSimpleName(_tableInfo.Schema) +
                                 _dialect.CompositeIdentifierSeparator
                               : "")
                           + _dialect.WrapSimpleName(_tableInfo.Name);

        _versionColumn = _tableInfo.Columns.Values.FirstOrDefault(itm => itm.IsVersion);

        // Cache compiled setters for audit fields once at construction time
        if (_hasAuditColumns)
        {
            if (_tableInfo.LastUpdatedOn?.PropertyInfo != null)
            {
                _auditLastUpdatedOnSetter = GetOrCreateSetter(_tableInfo.LastUpdatedOn.PropertyInfo);
            }

            if (_tableInfo.LastUpdatedBy?.PropertyInfo != null)
            {
                _auditLastUpdatedBySetter = GetOrCreateSetter(_tableInfo.LastUpdatedBy.PropertyInfo);
            }

            if (_tableInfo.CreatedOn?.PropertyInfo != null)
            {
                _auditCreatedOnSetter = GetOrCreateSetter(_tableInfo.CreatedOn.PropertyInfo);
            }

            if (_tableInfo.CreatedBy?.PropertyInfo != null)
            {
                _auditCreatedBySetter = GetOrCreateSetter(_tableInfo.CreatedBy.PropertyInfo);
            }
        }
    }

    // =========================================================================
    // Initialization (Internal helpers only)
    // =========================================================================

    // =========================================================================
    // Load methods
    // =========================================================================

    /// <inheritdoc/>
    public ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc)
    {
        return LoadSingleAsync(sc, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken cancellationToken)
    {
        if (sc == null)
        {
            throw new ArgumentNullException(nameof(sc));
        }

        await using var reader = await sc.ExecuteReaderSingleRowAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var plan = GetOrBuildRecordsetPlan(reader);
            return MapReaderToObjectWithPlan(reader, plan);
        }

        return null;
    }

    /// <inheritdoc/>
    public ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc)
    {
        return LoadListAsync(sc, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken cancellationToken)
    {
        if (sc == null)
        {
            throw new ArgumentNullException(nameof(sc));
        }

        var list = new List<TEntity>();

        await using var reader = await sc.ExecuteReaderAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);

        HybridRecordsetPlan? plan = null;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
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

        HybridRecordsetPlan? plan = null;

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
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

        await using var reader =
            await sc.ExecuteReaderAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);

        HybridRecordsetPlan? plan = null;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
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

    // =========================================================================
    // Shared helper methods
    // =========================================================================

    internal BoundedCache<string, string> GetOrCreateQueryCache(ISqlDialect dialect) =>
        _queryCache.GetOrAdd(dialect.DatabaseType, static _ => new BoundedCache<string, string>(MaxCacheSize));

    internal BoundedCache<string, string[]> GetOrCreateParamNamesCache(ISqlDialect dialect) =>
        _whereParameterNames.GetOrAdd(dialect.DatabaseType,
            static _ => new BoundedCache<string, string[]>(MaxCacheSize));

    private static int ResolveReaderPlanCacheSize(IDatabaseContext context)
    {
        int? configured = null;
        try
        {
            configured = context.ReaderPlanCacheSize;
        }
        catch
        {
            // Ignore fallback property access failures (e.g., strict mocks).
        }

        if (configured is int size && size > 0)
        {
            return size;
        }

        return DefaultReaderPlanCapacity;
    }

    protected string BuildWrappedTableName(ISqlDialect dialect)
    {
        return _wrappedTableNameCache.GetOrAdd(dialect, d =>
        {
            if (string.IsNullOrWhiteSpace(_tableInfo.Schema) || !d.SupportsNamespaces)
            {
                return d.WrapSimpleName(_tableInfo.Name);
            }

            var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
            try
            {
                sb.Append(d.WrapSimpleName(_tableInfo.Schema));
                sb.Append(d.CompositeIdentifierSeparator);
                sb.Append(d.WrapSimpleName(_tableInfo.Name));
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        });
    }

    protected static ISqlDialect GetDialect(IDatabaseContext ctx)
    {
        return ctx.GetDialect();
    }

    protected void CheckParameterLimit(ISqlContainer sc, int? toAdd)
    {
        var count = sc.ParameterCount + (toAdd ?? 0);
        if (count > _context.MaxParameterLimit)
        {
            throw new TooManyParametersException("Too many parameters", _context.MaxParameterLimit);
        }
    }

    internal IReadOnlyList<IColumnInfo> GetCachedInsertableColumns()
    {
        if (_columnListCache.TryGet("Insertable", out var cached))
        {
            return cached;
        }

        var insertable = new List<IColumnInfo>(_tableInfo.OrderedColumns.Count);
        foreach (var c in _tableInfo.OrderedColumns)
        {
            if (!c.IsNonInsertable && (!c.IsId || c.IsIdWritable))
            {
                insertable.Add(c);
            }
        }

        return _columnListCache.GetOrAdd("Insertable", _ => insertable);
    }

    protected static IReadOnlyList<IReadOnlyList<T>> ChunkList<T>(
        IReadOnlyList<T> list, int paramsPerRow, int maxParameterLimit, int maxRowsPerBatch)
    {
        if (maxParameterLimit <= 0 || paramsPerRow <= 0)
        {
            return new List<IReadOnlyList<T>> { list };
        }

        var usableParams = (int)(maxParameterLimit * 0.9);
        var rowsPerChunkByParams = Math.Max(1, usableParams / Math.Max(1, paramsPerRow));
        var rowsPerChunk = Math.Min(rowsPerChunkByParams, maxRowsPerBatch > 0 ? maxRowsPerBatch : int.MaxValue);

        if (list.Count <= rowsPerChunk)
        {
            return new List<IReadOnlyList<T>> { list };
        }

        var chunks = new List<IReadOnlyList<T>>();
        for (var i = 0; i < list.Count; i += rowsPerChunk)
        {
            var end = Math.Min(i + rowsPerChunk, list.Count);
            var chunk = new List<T>(end - i);
            for (var j = i; j < end; j++)
            {
                chunk.Add(list[j]);
            }

            chunks.Add(chunk);
        }

        return chunks;
    }
}
