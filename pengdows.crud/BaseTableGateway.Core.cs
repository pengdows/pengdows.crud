// =============================================================================
// FILE: BaseTableGateway.Core.cs
// PURPOSE: Abstract base class for all table gateway variants.
//          Contains identity-neutral shared code: fields, initialization,
//          load methods, and helpers used by both PrimaryKeyTableGateway<TEntity>
//          and TableGateway<TEntity, TRowID>.
//
// AI SUMMARY:
// - Shared fields: context, dialect, tableInfo, caches, audit setters.
// - protected constructor: accepts IDatabaseContext + optional audit resolver and
//   sets up all shared state (no [Id]-specific logic).
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

    // Cached compiled setters for audit fields — initialized once in the constructor
    private Action<object, object?>? _auditLastUpdatedOnSetter;
    private Action<object, object?>? _auditLastUpdatedBySetter;
    private Action<object, object?>? _auditCreatedOnSetter;
    private Action<object, object?>? _auditCreatedBySetter;

    // =========================================================================
    // Cache instance fields
    // =========================================================================

    internal readonly BoundedCache<string, IReadOnlyList<IColumnInfo>> _columnListCache = new(MaxCacheSize);

    // Keyed by dialect INSTANCE (not the SupportedDatabase enum) so two contexts on the same
    // product but different server versions never share stale SQL strings. A ConditionalWeakTable
    // (rather than a ConcurrentDictionary) ties each entry's lifetime to its dialect instance, so
    // the cache doesn't grow without bound as contexts are created/disposed over a long-running
    // process's lifetime.
    private readonly ConditionalWeakTable<ISqlDialect, BoundedCache<string, string>> _queryCache = new();

    private readonly ConditionalWeakTable<ISqlDialect, BoundedCache<string, string[]>> _whereParameterNames =
        new();

    // Cache for wrapped table names per dialect instance (same leak-safety rationale as above)
    private readonly ConditionalWeakTable<ISqlDialect, string> _wrappedTableNameCache = new();

    // Thread-safe cache for hybrid reader plans by structural recordset shape (not a bare hash,
    // so a hash collision between two shapes can never reuse the wrong compiled mapper) and by the
    // reading context's coercion options: a gateway reads through any tenant's context (REV-033).
    private BoundedCache<ReaderPlanKey, HybridRecordsetPlan> _readerPlans =
        new(DefaultReaderPlanCapacity);

    private readonly record struct ReaderPlanKey(RecordsetShape Shape, TypeCoercionOptions Options);

    // =========================================================================
    // Properties
    // =========================================================================

    /// <inheritdoc/>
    public string WrappedTableName { get; init; } = null!;

    /// <inheritdoc cref="IPrimaryKeyTableGateway{TEntity}.ColumnName"/>
    public string ColumnName(string propertyName) =>
        ColumnNameResolver.Resolve(_tableInfo, typeof(TEntity), propertyName);

    /// <inheritdoc/>
    public EnumParseFailureMode EnumParseBehavior { get; init; }

    /// <inheritdoc/>
    public AuditCreationPolicy AuditCreationPolicy { get; init; } = AuditCreationPolicy.PreserveExplicitValues;

    protected IDatabaseContext Context => _context;

    // =========================================================================
    // Nested types
    // =========================================================================

    // Hybrid plan: Monolithic compiled expression that handles all columns.
    // Compiled once per schema shape for maximum performance.
    private sealed class HybridRecordsetPlan
    {
        public Func<ITrackedReader, TEntity> CompiledMapper { get; }

        /// <summary>
        /// The recordset shape this plan was compiled for. It travels with the plan so the hot slot
        /// is one reference: a shape and a plan stored separately can be read torn by a racing load.
        /// </summary>
        public RecordsetShape Shape { get; }

        /// <summary>The coercion options the plan was compiled with (the reading context's).</summary>
        public TypeCoercionOptions Options { get; }

        public HybridRecordsetPlan(Func<ITrackedReader, TEntity> compiledMapper, RecordsetShape shape,
            TypeCoercionOptions options)
        {
            CompiledMapper = compiledMapper ?? throw new ArgumentNullException(nameof(compiledMapper));
            Shape = shape;
            Options = options;
        }
    }

    // =========================================================================
    // Constructor
    // =========================================================================

    /// <summary>
    /// Base constructor: stores audit resolver, optionally updates static logger,
    /// then resolves the dialect, table metadata, wrapped table name, and audit setters.
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
        // All of the dialect's read options (copying fields one by one dropped every flag added later).
        _coercionOptions = TypeCoercionOptions.For(_dialect);
        _readerPlans = new BoundedCache<ReaderPlanKey, HybridRecordsetPlan>(ResolveReaderPlanCacheSize(databaseContext));

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
    // Load methods
    // =========================================================================

    // Gateway hydration's reader (DEC-013): a SqlContainer opens it with SequentialAccess where the
    // dialect asks; any other ISqlContainer implementation keeps its own behavior.
    private static ValueTask<ITrackedReader> OpenHydrationReaderAsync(ISqlContainer sc, bool singleRow,
        CancellationToken cancellationToken)
    {
        if (sc is SqlContainer container)
        {
            return container.ExecuteReaderForHydrationAsync(singleRow, cancellationToken);
        }

        return singleRow
            ? sc.ExecuteReaderSingleRowAsync(cancellationToken)
            : sc.ExecuteReaderAsync(CommandType.Text, cancellationToken);
    }

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

        await using var reader = await OpenHydrationReaderAsync(sc, singleRow: true, cancellationToken).ConfigureAwait(false);
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

        await using var reader = await OpenHydrationReaderAsync(sc, singleRow: false, cancellationToken).ConfigureAwait(false);

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
            await OpenHydrationReaderAsync(sc, singleRow: false, CancellationToken.None).ConfigureAwait(false);

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
            await OpenHydrationReaderAsync(sc, singleRow: false, cancellationToken).ConfigureAwait(false);

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
        _queryCache.GetValue(dialect, static _ => new BoundedCache<string, string>(MaxCacheSize));

    internal BoundedCache<string, string[]> GetOrCreateParamNamesCache(ISqlDialect dialect) =>
        _whereParameterNames.GetValue(dialect,
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

    /// <summary>
    /// Every cell of a batch chunk, read once, row-major (<c>row * columns.Count + column</c>): the
    /// dialect's SQL builder and the binding loop both read it. Reading each cell twice serialized
    /// a [Json] value twice per row (PERF-012).
    /// </summary>
    internal static object?[] ExtractBatchCells(IReadOnlyList<TEntity> chunk, IReadOnlyList<IColumnInfo> columns)
    {
        var cells = new object?[chunk.Count * columns.Count];
        for (var row = 0; row < chunk.Count; row++)
        {
            var entity = chunk[row];
            for (var col = 0; col < columns.Count; col++)
            {
                cells[row * columns.Count + col] = columns[col].MakeParameterValueFromField(entity);
            }
        }

        return cells;
    }

    protected string BuildWrappedTableName(ISqlDialect dialect)
    {
        // Fast path first: GetValue allocates its factory delegate (and closure) even on a hit.
        if (_wrappedTableNameCache.TryGetValue(dialect, out var cached))
        {
            return cached;
        }

        return _wrappedTableNameCache.GetValue(dialect, d =>
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

    // TYPE-020: per dialect instance, which operations need the table's declared column types and
    // whether this gateway has applied them. Its SQL caches are rebuilt once, when applied.
    private sealed class DeclaredTypeState
    {
        public bool ReadNeeded;
        public bool WriteNeeded;
        public volatile bool Applied;
    }

    private readonly ConditionalWeakTable<ISqlDialect, DeclaredTypeState> _declaredTypeStates = new();

    // The fast path is a lookup only: the lambdas live in AddDeclaredTypeState, so their closure
    // (allocated at method entry) isn't paid on every call.
    private DeclaredTypeState DeclaredTypeStateFor(SqlDialect dialect) =>
        _declaredTypeStates.TryGetValue(dialect, out var state) ? state : AddDeclaredTypeState(dialect);

    private DeclaredTypeState AddDeclaredTypeState(SqlDialect dialect)
    {
        var state = new DeclaredTypeState
        {
            ReadNeeded = _tableInfo.OrderedColumns.Any(c => dialect.NeedsDeclaredType(c, forRead: true)),
            WriteNeeded = _tableInfo.OrderedColumns.Any(c => dialect.NeedsDeclaredType(c, forRead: false))
        };
        state.Applied = !state.ReadNeeded && !state.WriteNeeded;
        return _declaredTypeStates.GetValue(dialect, _ => state);
    }

    /// <summary>
    /// True when a write (or, with <paramref name="forRead"/>, a read) on this context needs the
    /// table's declared column types and this gateway hasn't applied them yet; the async entry points
    /// then call <see cref="EnsureDeclaredTypesAsync"/> before building SQL.
    /// </summary>
    private protected bool DeclaredTypesPending(IDatabaseContext ctx, bool forRead = false) =>
        GetDialect(ctx) is SqlDialect dialect && DeclaredTypeStateFor(dialect) is { Applied: false } state &&
        (forRead ? state.ReadNeeded : state.WriteNeeded);

    /// <summary>
    /// TYPE-020: learns the declared database type of every column the dialect asks about
    /// (<c>SqlDialect.NeedsDeclaredType</c>) once per table and context, from the provider's metadata
    /// for "SELECT cols FROM table WHERE 1 = 0", then rebuilds this gateway's cached SQL for the
    /// dialect. A failed probe (permissions, a write-only context) is logged and changes nothing.
    /// </summary>
    private protected ValueTask EnsureDeclaredTypesAsync(IDatabaseContext ctx, CancellationToken cancellationToken,
        bool forRead = false) =>
        DeclaredTypesPending(ctx, forRead) ? EnsureDeclaredTypesSlowAsync(ctx, cancellationToken) : default;

    private async ValueTask EnsureDeclaredTypesSlowAsync(IDatabaseContext ctx, CancellationToken cancellationToken)
    {
        var dialect = (SqlDialect)GetDialect(ctx);
        var needed = _tableInfo.OrderedColumns
            .Where(c => dialect.NeedsDeclaredType(c, forRead: true) || dialect.NeedsDeclaredType(c, forRead: false))
            .ToList();
        var table = BuildWrappedTableName(dialect);
        if (!dialect.HasProbedDeclaredTypes(table))
        {
            dialect.RecordDeclaredTypes(table, await ProbeDeclaredTypesAsync(ctx, dialect, table, needed, cancellationToken)
                .ConfigureAwait(false));
        }

        ResetDialectCaches(dialect);
        DeclaredTypeStateFor(dialect).Applied = true;
    }

    private static async ValueTask<List<(IColumnInfo, string)>> ProbeDeclaredTypesAsync(IDatabaseContext ctx,
        SqlDialect dialect, string table, IReadOnlyList<IColumnInfo> columns, CancellationToken cancellationToken)
    {
        var found = new List<(IColumnInfo, string)>(columns.Count);
        try
        {
            await using var sc = ctx.CreateSqlContainer();
            sc.Query.Append("SELECT ");
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(", ");
                }

                sc.Query.Append(dialect.WrapSimpleName(columns[i].Name));
            }

            sc.Query.Append(" FROM ").Append(table).Append(" WHERE 1 = 0");
            await using var reader = await sc.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
            // By name: the result's columns are the ones selected, but matching names doesn't depend on it.
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var column = columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                var declared = column == null ? null : reader.GetDataTypeName(i);
                if (!string.IsNullOrEmpty(declared))
                {
                    found.Add((column!, declared));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "Could not read the declared column types of {Table}; writing it without them", table);
            found.Clear();
        }

        return found;
    }

    /// <summary>
    /// Drops this gateway's cached SQL for <paramref name="dialect"/> once its declared column types
    /// are known, so it is rebuilt with them. Derived gateways drop their own caches too.
    /// </summary>
    private protected virtual void ResetDialectCaches(ISqlDialect dialect)
    {
        _queryCache.Remove(dialect);
        _whereParameterNames.Remove(dialect);
    }

    /// <summary>
    /// DEC-012: the ON DUPLICATE KEY UPDATE fragment with each column whose incoming-row reference
    /// the dialect can't trust set from that column's own parameter instead. Returns the fragment
    /// unchanged when no column is affected.
    /// </summary>
    /// <summary>
    /// The MERGE pieces for a dialect that binds values directly (<c>SqlDialect.MergeBindsValuesDirectly</c>,
    /// WRT-004): the source holds <paramref name="keyColumns"/> (plus any column the dialect sources,
    /// <c>SqlDialect.MergeSourcesColumn</c>); UPDATE SET and INSERT VALUES use
    /// each other column's parameter as a <c>{P}name</c> token, so a value used in both is bound at
    /// each use, in order, on a positional provider.
    /// </summary>
    private protected static (string Source, string UpdateSet, string InsertValues) RenderDirectBoundMerge(
        ISqlDialect dialect, IReadOnlyList<IColumnInfo> columns, IReadOnlyList<string> parameterNames,
        IReadOnlyList<IColumnInfo> keyColumns, IReadOnlyList<IColumnInfo> updateColumns)
    {
        var sqlDialect = (SqlDialect)dialect;
        string Token(int i)
        {
            var token = "{P}" + parameterNames[i];
            return sqlDialect.RendersColumnArgument(columns[i]) ? sqlDialect.RenderColumnArgument(token, columns[i]) : token;
        }

        var index = new Dictionary<IColumnInfo, int>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            index[columns[i]] = i;
        }

        var keys = new HashSet<IColumnInfo>(keyColumns);
        var sourceColumns = new List<IColumnInfo>(keyColumns);
        foreach (var column in columns)
        {
            if (!keys.Contains(column) && sqlDialect.MergeSourcesColumn(column))
            {
                sourceColumns.Add(column);
            }
        }

        var sourcePlaceholders = new string[sourceColumns.Count];
        for (var i = 0; i < sourceColumns.Count; i++)
        {
            sourcePlaceholders[i] = Token(index[sourceColumns[i]]);
        }

        var inSource = new HashSet<IColumnInfo>(sourceColumns);

        var targetAlias = dialect.MergeUpdateRequiresTargetAlias ? "t." : "";
        var update = new System.Text.StringBuilder();
        foreach (var column in updateColumns)
        {
            if (update.Length > 0)
            {
                update.Append(", ");
            }

            update.Append(targetAlias).Append(dialect.WrapSimpleName(column.Name)).Append(" = ")
                .Append(inSource.Contains(column) ? "s." + dialect.WrapSimpleName(column.Name) : Token(index[column]));
        }

        var values = new System.Text.StringBuilder();
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                values.Append(", ");
            }

            values.Append(inSource.Contains(columns[i]) ? "s." + dialect.WrapSimpleName(columns[i].Name) : Token(i));
        }

        return (sqlDialect.RenderMergeSourceFromPlaceholders(sourceColumns, sourcePlaceholders), update.ToString(), values.ToString());
    }

    private protected static string ReuseParametersForUnreliableIncoming(ISqlDialect dialect, string fragment,
        IReadOnlyList<IColumnInfo> columns, IReadOnlyList<string> parameterNames) =>
        ReuseParametersForUnreliableIncoming(dialect, fragment, columns, parameterNames, static name => name);

    // The parameters themselves: a name is read only for an unreliable column, so no list of names is built
    // for every upsert on every MySQL-family dialect (REV-082).
    private protected static string ReuseParametersForUnreliableIncoming(ISqlDialect dialect, string fragment,
        IReadOnlyList<IColumnInfo> columns, IReadOnlyList<System.Data.Common.DbParameter> parameters) =>
        ReuseParametersForUnreliableIncoming(dialect, fragment, columns, parameters, static p => p.ParameterName);

    private static string ReuseParametersForUnreliableIncoming<T>(ISqlDialect dialect, string fragment,
        IReadOnlyList<IColumnInfo> columns, IReadOnlyList<T> parameters, Func<T, string> nameOf)
    {
        if (dialect is not SqlDialect sqlDialect)
        {
            return fragment;
        }

        for (var i = 0; i < columns.Count && i < parameters.Count; i++)
        {
            if (!sqlDialect.UpsertIncomingValueUnreliable(columns[i]))
            {
                continue;
            }

            var wrapped = dialect.WrapSimpleName(columns[i].Name);
            fragment = fragment.Replace(
                string.Concat(wrapped, " = ", dialect.UpsertIncomingColumn(columns[i].Name)),
                string.Concat(wrapped, " = ", dialect.MakeParameterName(nameOf(parameters[i]))),
                StringComparison.Ordinal);
        }

        return fragment;
    }

    /// <summary>
    /// DEC-012: true when the update fragment reads a column whose incoming-row reference the dialect
    /// can't trust; a batch upsert then runs one statement per row (a batch can only use that reference).
    /// </summary>
    private protected static bool UpsertFragmentHasUnreliableIncoming(ISqlDialect dialect, string? fragment,
        IEnumerable<IColumnInfo> columns)
    {
        if (fragment == null || dialect is not SqlDialect sqlDialect)
        {
            return false;
        }

        foreach (var column in columns)
        {
            if (sqlDialect.UpsertIncomingValueUnreliable(column) &&
                fragment.Contains(dialect.UpsertIncomingColumn(column.Name), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a rows-affected shortfall from a batch-upsert container reliably means a
    /// <c>[Version]</c> conflict. Mirrors the SQL shape <c>BuildBatchUpsert</c> picks:
    /// chunked ON CONFLICT carries a version guard only when the dialect supports
    /// <c>DO UPDATE ... WHERE</c>; chunked ON DUPLICATE KEY (MySQL family) has no guard and
    /// reports 0 affected for an unchanged row, so it can't detect a conflict; the per-entity
    /// fallback uses the same rule as single-entity <c>UpsertAsync</c> (a guarded MERGE whose rows
    /// affected reveals a skipped row; not Firebird's unguarded UPDATE OR INSERT or Sybase ASE, see
    /// <see cref="IInternalSqlDialect.MergeUpsertReportsSkippedVersionRow"/>).
    /// </summary>
    private protected bool BatchUpsertCanDetectVersionConflict(IDatabaseContext ctx)
    {
        if (_versionColumn == null || _versionColumn.IsOpaqueVersionColumn())
        {
            return false;
        }

        var info = ctx.DataSourceInfo;
        if (info.SupportsInsertOnConflict)
        {
            return GetDialect(ctx).SupportsOnConflictWhere;
        }

        if (info.SupportsOnDuplicateKey)
        {
            return false;
        }

        return info.SupportsMerge && GetDialect(ctx).MergeUpsertReportsSkippedVersionRow();
    }

    protected void CheckParameterLimit(ISqlContainer sc, int? toAdd)
    {
        var maxParameterLimit = sc is ISqlDialectProvider dialectProvider
            ? dialectProvider.Dialect.MaxParameterLimit
            : _context.MaxParameterLimit;
        var count = sc.ParameterCount + (toAdd ?? 0);
        if (count > maxParameterLimit)
        {
            throw new TooManyParametersException("Too many parameters", maxParameterLimit);
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

    /// <summary>
    /// Refuses a MERGE upsert of a [Version] entity on a dialect whose MERGE cannot express the
    /// optimistic-concurrency check (see <see cref="IInternalSqlDialect.SupportsMergeMatchedCondition"/>).
    /// Called before any audit/version field is mutated.
    /// </summary>
    private protected void ThrowIfVersionedMergeUpsertUnsupported(ISqlDialect dialect)
    {
        if (_versionColumn != null && !_versionColumn.IsOpaqueVersionColumn()
            && !dialect.SupportsMergeMatchedCondition())
        {
            throw new NotSupportedException(
                $"Upsert of '{typeof(TEntity).Name}' is not supported on {dialect.DatabaseType}: its MERGE has no " +
                $"conditional matched clause, so the [Version] column '{_versionColumn.Name}' optimistic-concurrency " +
                "check cannot be expressed. Use CreateAsync/UpdateAsync instead.");
        }
    }

    /// <summary>
    /// A column reference for use inside an expression of an unaliased statement (UPDATE/DELETE
    /// predicate, SET right-hand side, unaliased SELECT): table-qualified where the dialect requires
    /// it (<see cref="IInternalSqlDialect.QualifiesColumnReferences"/>), otherwise the bare column.
    /// </summary>
    private protected string WrapColumnReference(ISqlDialect dialect, string columnName)
    {
        return ColumnReferencePrefix(string.Empty, dialect) + dialect.WrapSimpleName(columnName);
    }

    /// <summary>
    /// Prefix for column references: the wrapped alias when one is given; with no alias, the
    /// wrapped table name where the dialect requires qualified references, otherwise empty.
    /// </summary>
    private protected string ColumnReferencePrefix(string alias, ISqlDialect dialect)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return dialect.WrapSimpleName(alias) + dialect.CompositeIdentifierSeparator;
        }

        return dialect.QualifiesColumnReferences()
            ? BuildWrappedTableName(dialect) + dialect.CompositeIdentifierSeparator
            : string.Empty;
    }

    /// <summary>
    /// A caller-supplied column name (count helpers) wrapped for a WHERE predicate: left as given
    /// when already qualified, table-qualified when the dialect requires it.
    /// </summary>
    private protected string WrapCallerColumnReference(ISqlDialect dialect, string column)
    {
        return column.Contains('.') || !dialect.QualifiesColumnReferences()
            ? dialect.WrapObjectName(column)
            : BuildWrappedTableName(dialect) + dialect.CompositeIdentifierSeparator + dialect.WrapObjectName(column);
    }
}
