// =============================================================================
// FILE: DataReaderMapper.cs
// PURPOSE: High-performance mapper that converts IDataReader rows to strongly
//          typed entity objects using cached execution plans.
//
// AI SUMMARY:
// - Maps DataReader columns to entity properties via compiled delegates.
// - Key methods:
//   * LoadAsync<T>() - Loads all rows into a List<T>
//   * StreamAsync<T>() - Returns IAsyncEnumerable<T> for streaming large results
//   * Instance - Singleton for typical use
// - Performance optimizations:
//   * Caches execution plans per (Type, schema shape, options) tuple
//   * Caches compiled setters per (Type, PropertyInfo, field type, coercion, enum mode,
//     ordinal) key
//   * Bounded LRU caches prevent unbounded memory growth
//   * BuildSchemaShape builds a structurally-equatable RecordsetShape (shared with
//     BaseTableGateway's reader-plan cache) instead of a bare hash, so a hash collision
//     can never reuse the wrong plan
//   * LoadInternalAsync uses a direct while(ReadAsync) loop with the plan
//     hoisted outside; per-row mapping is a simple delegate call (MapSingleRow)
// - Column matching:
//   * By default matches columns to properties by name (case-insensitive)
//   * With ColumnsOnly=true, only maps [Column]-attributed properties
// - Type coercion: Uses TypeCoercionHelper for automatic type conversion.
// - Enum handling: Configurable via MapperOptions.EnumMode (EnumParseFailureMode).
// - Null handling: DBNull leaves nullable/reference-type properties unset (null). For
//   non-nullable value types the typed getter throws: DataMappingException when Strict,
//   otherwise a logged warning and the property keeps its default.
// - Thread-safe: All caches use thread-safe data structures.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// High-performance mapper for converting <see cref="ITrackedReader"/> rows to strongly-typed entities.
/// </summary>
/// <remarks>
/// <para>
/// This mapper uses cached execution plans and compiled delegates to achieve near-manual
/// performance when mapping database results to entities.
/// </para>
/// <para>
/// <strong>Cache Management:</strong> Execution plans are cached based on the combination of
/// entity type, reader schema (column names/types), and mapping options. Bounded LRU caches
/// prevent unbounded memory growth.
/// </para>
/// <para>
/// <strong>Streaming:</strong> Use <see cref="StreamAsync{T}"/> for large result sets to avoid
/// loading all rows into memory at once.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Load all results
/// await using var reader = await container.ExecuteReaderAsync();
/// var entities = await DataReaderMapper.LoadAsync&lt;MyEntity&gt;(reader, MapperOptions.Default);
///
/// // Stream large results
/// await foreach (var entity in DataReaderMapper.StreamAsync&lt;MyEntity&gt;(reader))
/// {
///     ProcessEntity(entity);
/// }
/// </code>
/// </example>
/// <seealso cref="IDataReaderMapper"/>
/// <seealso cref="MapperOptions"/>
/// <seealso cref="TypeCoercionHelper"/>
public sealed class DataReaderMapper : IDataReaderMapper
{
    public static readonly IDataReaderMapper Instance = new DataReaderMapper();

    // Cache capacity limits to prevent unbounded memory growth with varied query shapes.
    // These are bounded LRU caches that evict least-recently-used entries when capacity is exceeded.
    private const int MaxPlanCacheSize = 128;
    private const int MaxPropertyLookupCacheSize = 64;

    private static readonly BoundedCache<PlanCacheKey, object> _planCache = new(MaxPlanCacheSize);

    private static readonly BoundedCache<PropertyLookupCacheKey, IReadOnlyDictionary<string, PropertyInfo>>
        _propertyLookupCache = new(MaxPropertyLookupCacheSize);

    private static readonly MethodInfo _getFieldValueGenericMethod = ReaderGetters.GetFieldValueDefinition;

    internal DataReaderMapper()
    {
    }

    private readonly record struct PlanCacheKey(
        Type Type,
        RecordsetShape Shape,
        bool ColumnsOnly,
        EnumParseFailureMode EnumMode,
        TypeCoercionOptions? Coercion)
    {
        public PlanCacheKey(Type type, RecordsetShape shape, bool columnsOnly, EnumParseFailureMode enumMode)
            : this(type, shape, columnsOnly, enumMode, null)
        {
        }
    }

    public static ValueTask<List<T>> LoadObjectsFromDataReaderAsync<T>(
        ITrackedReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return LoadInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    public static ValueTask<List<T>> LoadAsync<T>(
        ITrackedReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return LoadInternalAsync<T>(reader, options, cancellationToken);
    }

    public static IAsyncEnumerable<T> StreamAsync<T>(
        ITrackedReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return StreamInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    public static IAsyncEnumerable<T> StreamAsync<T>(
        ITrackedReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return StreamInternalAsync<T>(reader, options, cancellationToken);
    }

    /// <summary>
    /// Resolves this reader's mapping plan once (shape inspection and plan-cache lookup) and returns the per-row
    /// mapper it runs, so a caller that maps many rows of one result set pays that resolution once. This is the
    /// DataReaderMapper path for arbitrary POCOs; entities mapped by a TableGateway use CompiledMapperFactory.
    /// </summary>
    internal static Func<DbDataReader, T> ResolveRowMapper<T>(DbDataReader reader, IMapperOptions options)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= MapperOptions.Default;

        var shape = BuildSchemaShape(reader, options);
        var planKey = new PlanCacheKey(typeof(T), shape, options.ColumnsOnly, options.EnumMode);
        var plan = GetPlan<T>(planKey, reader, options);
        return CompileRowMapper(plan.Columns, options.Strict);
    }

    internal static ValueTask<List<T>> LoadObjectsFromDataReaderAsync<T>(
        IDataReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return LoadInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    internal static ValueTask<List<T>> LoadAsync<T>(
        IDataReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return LoadInternalAsync<T>(reader, options, cancellationToken);
    }

    internal static IAsyncEnumerable<T> StreamAsync<T>(
        IDataReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return StreamInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    internal static IAsyncEnumerable<T> StreamAsync<T>(
        IDataReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return StreamInternalAsync<T>(reader, options, cancellationToken);
    }

    ValueTask<List<T>> IDataReaderMapper.LoadAsync<T>(
        ITrackedReader reader,
        CancellationToken cancellationToken)
    {
        return LoadInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    ValueTask<List<T>> IDataReaderMapper.LoadAsync<T>(
        ITrackedReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken)
    {
        return LoadInternalAsync<T>(reader, options, cancellationToken);
    }

    IAsyncEnumerable<T> IDataReaderMapper.StreamAsync<T>(
        ITrackedReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken)
    {
        return StreamInternalAsync<T>(reader, options, cancellationToken);
    }

    private static async ValueTask<List<T>> LoadInternalAsync<T>(
        ITrackedReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken)
        where T : class, new()
    {
        var recordReader = GetRecordReader(reader, out var unresolved, out var coercion, out var unresolvedReader);
        options ??= MapperOptions.Default;

        var shape = BuildSchemaShape(recordReader, options, unresolved);
        var planKey = new PlanCacheKey(typeof(T), shape, options.ColumnsOnly, options.EnumMode, coercion);
        var plan = GetPlan<T>(planKey, recordReader, options, unresolved, coercion, unresolvedReader);

        var result = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(MapSingleRow(recordReader, plan, options));
        }

        return result;
    }

    private static async ValueTask<List<T>> LoadInternalAsync<T>(
        IDataReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader is ITrackedReader trackedReader)
        {
            return await LoadInternalAsync<T>(trackedReader, options, cancellationToken).ConfigureAwait(false);
        }

        if (reader is not DbDataReader rdr)
        {
            throw new ArgumentException("reader must be DbDataReader", nameof(reader));
        }

        options ??= MapperOptions.Default;

        var shape = BuildSchemaShape(rdr, options);
        var planKey = new PlanCacheKey(typeof(T), shape, options.ColumnsOnly, options.EnumMode);
        var plan = GetPlan<T>(planKey, rdr, options);

        var result = new List<T>();

        while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(MapSingleRow(rdr, plan, options));
        }

        return result;
    }

    private static async IAsyncEnumerable<T> StreamInternalAsync<T>(
        ITrackedReader reader,
        IMapperOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class, new()
    {
        var recordReader = GetRecordReader(reader, out var unresolved, out var coercion, out var unresolvedReader);
        options ??= MapperOptions.Default;

        var shape = BuildSchemaShape(recordReader, options, unresolved);
        var planKey = new PlanCacheKey(typeof(T), shape, options.ColumnsOnly, options.EnumMode, coercion);
        var plan = GetPlan<T>(planKey, recordReader, options, unresolved, coercion, unresolvedReader);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return MapSingleRow(recordReader, plan, options);
        }
    }

    private static async IAsyncEnumerable<T> StreamInternalAsync<T>(
        IDataReader reader,
        IMapperOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader is ITrackedReader trackedReader)
        {
            await foreach (var item in StreamInternalAsync<T>(trackedReader, options, cancellationToken))
            {
                yield return item;
            }
            yield break;
        }

        if (reader is not DbDataReader rdr)
        {
            throw new ArgumentException("reader must be DbDataReader", nameof(reader));
        }

        options ??= MapperOptions.Default;

        var shape = BuildSchemaShape(rdr, options);
        var planKey = new PlanCacheKey(typeof(T), shape, options.ColumnsOnly, options.EnumMode);

        var plan = GetPlan<T>(planKey, rdr, options);

        while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return MapSingleRow(rdr, plan, options);
        }
    }

    // A cached plan is found without allocating: GetOrAdd's factory closure (over the reader and
    // options) was allocated on every call, hit or miss (PERF-014). The miss paths are separate
    // methods so that closure exists only when a plan is built.
    private static MapperPlan<T> GetPlan<T>(PlanCacheKey planKey, DbDataReader reader, IMapperOptions options,
        Func<int, Type?>? unresolved, TypeCoercionOptions? coercion,
        Func<IDataRecord, int, Type, object>? unresolvedReader) where T : class, new() =>
        _planCache.TryGet(planKey, out var cached)
            ? (MapperPlan<T>)cached
            : AddPlan<T>(planKey, reader, options, unresolved, coercion, unresolvedReader);

    private static MapperPlan<T> AddPlan<T>(PlanCacheKey planKey, DbDataReader reader, IMapperOptions options,
        Func<int, Type?>? unresolved, TypeCoercionOptions? coercion,
        Func<IDataRecord, int, Type, object>? unresolvedReader) where T : class, new() =>
        (MapperPlan<T>)_planCache.GetOrAdd(planKey, _ => BuildPlan<T>(reader, options, unresolved, coercion, unresolvedReader));

    private static MapperPlan<T> GetPlan<T>(PlanCacheKey planKey, DbDataReader reader, IMapperOptions options)
        where T : class, new() =>
        _planCache.TryGet(planKey, out var cached)
            ? (MapperPlan<T>)cached
            : AddPlan<T>(planKey, reader, options);

    private static MapperPlan<T> AddPlan<T>(PlanCacheKey planKey, DbDataReader reader, IMapperOptions options)
        where T : class, new() =>
        (MapperPlan<T>)_planCache.GetOrAdd(planKey, _ => BuildPlan<T>(reader, options));

    // new T() in generic code compiles to Activator.CreateInstance<T>(); a compiled constructor call
    // per type is cheaper on the per-row path (PERF-014).
    private static class Factory<T> where T : class, new()
    {
        public static readonly Func<T> Create =
            System.Linq.Expressions.Expression.Lambda<Func<T>>(System.Linq.Expressions.Expression.New(typeof(T))).Compile();
    }

    private static DbDataReader GetRecordReader(ITrackedReader reader, out Func<int, Type?>? unresolved,
        out TypeCoercionOptions? coercion, out Func<IDataRecord, int, Type, object>? unresolvedReader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        unresolved = null;
        coercion = null;
        unresolvedReader = null;

        if (reader is IInternalTrackedReader internalReader)
        {
            // TYPE-016: the tracked reader knows which columns its dialect reads although the
            // provider reports no field type for them (hierarchyid without Microsoft.SqlServer.Types).
            unresolved = internalReader.GetUnresolvedColumnType;
            // TYPE-002: provider-specific coercions (e.g. MySQL's SRID-prefixed spatial format)
            // apply here as they do through the gateway.
            coercion = internalReader.CoercionOptions;
            unresolvedReader = internalReader.UnresolvedColumnReadDelegate;
            return internalReader.InnerReader;
        }

        if (reader is DbDataReader dbReader)
        {
            return dbReader;
        }

        throw new ArgumentException("reader must expose an underlying DbDataReader", nameof(reader));
    }

    /// <summary>
    /// Maps the current reader row to a single entity using a pre-built plan.
    /// Called by both LoadInternalAsync (direct loop) and StreamInternalAsync
    /// to keep per-row logic in one place.
    /// </summary>
    private static T MapSingleRow<T>(DbDataReader rdr, MapperPlan<T> plan, IMapperOptions options)
        where T : class, new()
    {
        return plan.RowMapper(rdr, options.Strict);
    }

    // A column that fails to read or convert: Strict raises a DataMappingException naming the column and property
    // (the cause kept); otherwise the failure is logged and the property keeps its default (the row's other columns
    // are still mapped). Called from the catch around each column in the compiled row body.
    private static void HandleColumnFailure(Exception ex, bool strict, DbDataReader rdr, int ordinal,
        PropertyInfo property)
    {
        if (strict)
        {
            throw new DataMappingException(
                $"Failed to map column '{rdr.GetName(ordinal)}' to property '{property.Name}'.",
                SupportedDatabase.Unknown,
                ex);
        }

        TypeCoercionHelper.Logger.LogWarning(
            ex,
            "Failed to map column '{Column}' to property '{Property}'.",
            rdr.GetName(ordinal),
            property.Name);
    }

    private static readonly MethodInfo _handleColumnFailureMethod =
        typeof(DataReaderMapper).GetMethod(nameof(HandleColumnFailure), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo _isDbNullMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.IsDBNull))!;

    private static MapperPlan<T> BuildPlan<T>(DbDataReader reader, IMapperOptions options,
        Func<int, Type?>? unresolved = null, TypeCoercionOptions? coercion = null,
        Func<IDataRecord, int, Type, object>? unresolvedReader = null)
        where T : class, new()
    {
        var type = typeof(T);
        var propertyLookup = GetPropertyLookup(type, options);
        var columns = new List<ColumnBinding<T>>();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);

            if (!options.ColumnsOnly && options.NamePolicy != null)
            {
                name = options.NamePolicy(name);
            }

            if (propertyLookup.TryGetValue(name, out var prop))
            {
                var fieldType = ResolveFieldType(reader, i, unresolved);
                var requiresCoercion = RequiresCoercion(fieldType, prop.PropertyType);
                // An Npgsql inet or interval column is read as Npgsql's own type, which the schema says once, here.
                var npgsqlTyped = ColumnQuirkReads.NpgsqlTypedFor(reader, i, prop.PropertyType);
                var key = new SetterCacheKey(typeof(T), prop, fieldType, requiresCoercion, options.EnumMode, i, coercion, npgsqlTyped);
                var isUnresolved = IsUnresolved(i, unresolved);
                // Non-nullable value types cannot hold null at the .NET level. Skip the IsDBNull
                // guard for these columns — if the DB returns NULL for a non-nullable property,
                // the typed getter throws (DataMappingException when Strict; otherwise logged).
                var propType = prop.PropertyType;
                var skipNullCheck = propType.IsValueType && Nullable.GetUnderlyingType(propType) == null;
                columns.Add(new ColumnBinding<T>(
                    key,
                    isUnresolved
                        ? CreateUnresolvedSetter<T>(prop, fieldType, options.EnumMode, i, coercion, unresolvedReader)
                        : null,
                    skipNullCheck));
            }
        }

        var bindings = columns.ToArray();
        return new MapperPlan<T>(bindings, CompileRowMapper<T>(bindings));
    }

    /// <summary>
    /// Compiles the whole row into one body (a new instance, then each mapped column read in place), so mapping a
    /// row makes no delegate call per column. <paramref name="fixedStrict"/> bakes Strict in; without it the
    /// compiled delegate takes it as an argument so one cached plan serves strict and lenient callers.
    /// </summary>
    private static Func<DbDataReader, bool, T> CompileRowMapper<T>(ColumnBinding<T>[] columns)
        where T : class, new()
    {
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var strict = Expression.Parameter(typeof(bool), "strict");
        var body = BuildRowBody(columns, reader, strict);
        return Expression.Lambda<Func<DbDataReader, bool, T>>(body, reader, strict).Compile();
    }

    private static Func<DbDataReader, T> CompileRowMapper<T>(ColumnBinding<T>[] columns, bool strict)
        where T : class, new()
    {
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var body = BuildRowBody(columns, reader, Expression.Constant(strict));
        return Expression.Lambda<Func<DbDataReader, T>>(body, reader).Compile();
    }

    private static Expression BuildRowBody<T>(ColumnBinding<T>[] columns, ParameterExpression reader,
        Expression strict) where T : class, new()
    {
        var obj = Expression.Variable(typeof(T), "obj");
        var steps = new List<Expression> { Expression.Assign(obj, Expression.New(typeof(T))) };

        foreach (var column in columns)
        {
            var key = column.Key;
            var handlesNull = column.UnresolvedSetter != null; // its closure checks IsDBNull itself
            Expression set = column.UnresolvedSetter != null
                ? Expression.Invoke(Expression.Constant(column.UnresolvedSetter), obj, reader)
                : BuildSetterBody<T>(key, obj, reader, out handlesNull);
            if (!column.SkipNullCheck && !handlesNull)
            {
                // Inside the try: a provider can fail on the null check itself (TYPE-005).
                set = Expression.IfThen(
                    Expression.Not(Expression.Call(reader, _isDbNullMethod, Expression.Constant(key.Ordinal))),
                    set);
            }

            var failure = Expression.Parameter(typeof(Exception), "ex");
            steps.Add(Expression.TryCatch(
                Expression.Block(typeof(void), set),
                Expression.Catch(
                    failure,
                    Expression.Block(
                        typeof(void),
                        Expression.Call(_handleColumnFailureMethod, failure, strict, reader,
                            Expression.Constant(key.Ordinal), Expression.Constant(key.Property))))));
        }

        steps.Add(obj);
        return Expression.Block(typeof(T), new[] { obj }, steps);
    }

    /// <summary>
    /// Builds the structural shape (post-NamePolicy names + resolved types) of the reader's
    /// schema for use as a plan-cache key. This used to be a bare 64-bit rolling hash, so two
    /// schemas that hash-collided silently shared one compiled plan; RecordsetShape's Equals
    /// lets the dictionary confirm the match instead of trusting the hash as unique.
    /// </summary>
    private static RecordsetShape BuildSchemaShape(DbDataReader reader, IMapperOptions options,
        Func<int, Type?>? unresolved = null)
    {
        var fieldCount = reader.FieldCount;
        var names = new string[fieldCount];
        var types = new Type[fieldCount];

        for (var i = 0; i < fieldCount; i++)
        {
            var name = reader.GetName(i);

            if (!options.ColumnsOnly && options.NamePolicy != null)
            {
                name = options.NamePolicy(name);
            }

            names[i] = name;
            types[i] = ResolveFieldType(reader, i, unresolved);
        }

        return new RecordsetShape(names, types, fieldCount);
    }

    // The expression that reads this column and assigns it to the property of objParam, for composing into the row body.
    // handlesNull: the expression reads the value once and treats DBNull as "leave the property alone" itself, so the
    // row body adds no IsDBNull call of its own.
    internal static Expression BuildSetterBody<T>(SetterCacheKey key, ParameterExpression objParam,
        ParameterExpression readerParam, out bool handlesNull)
    {
        handlesNull = false;
        var propertyAccess = Expression.Property(objParam, key.Property);
        var targetType = key.Property.PropertyType;
        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (ColumnStatementReads.TryBuild(readerParam, key.Ordinal, key.FieldType, targetType, key.Coercion,
                propertyAccess) is { } statement)
        {
            // Its one GetValue is also the null check (BuildRowBody adds no IsDBNull for this column).
            handlesNull = true;
            return statement;
        }

        if (ColumnQuirkReads.TryBuild(Expression.Convert(readerParam, typeof(IDataRecord)), key.Ordinal, key.FieldType,
                targetType, key.Coercion, key.NpgsqlTyped) is { } quirk)
        {
            return Expression.Assign(propertyAccess, quirk);
        }

        Expression valueExpression;
        if (underlyingTarget == typeof(Stream) && typeof(Stream).IsAssignableFrom(key.FieldType))
        {
            // DuckDB returns BLOBs as UnmanagedMemoryStream instances backed by the active reader;
            // copy them while the reader is valid instead of assigning the provider stream.
            var getValueMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;
            var rawValue = Expression.Call(readerParam, getValueMethod, Expression.Constant(key.Ordinal));
            var materialize = typeof(ProviderStreamMaterializer).GetMethod(nameof(ProviderStreamMaterializer.Materialize))!;
            valueExpression = Expression.Convert(Expression.Call(materialize, rawValue), targetType);
        }
        else if (underlyingTarget.IsEnum && EnumFastPath.Reads(key.FieldType))
        {
            // An integer column into an enum: a defined member is converted with no boxing; any other value is
            // converted by the full conversion (the same one used when the column is not an integer), so its
            // rules and failure modes are unchanged.
            var integer = Expression.Convert(
                Expression.Call(readerParam, TypedGetter(key.FieldType) ??
                                             _getFieldValueGenericMethod.MakeGenericMethod(key.FieldType),
                    Expression.Constant(key.Ordinal)),
                typeof(long));
            var fullConversion = TypeCoercionHelper.ResolveCoercer(key.FieldType, targetType, key.EnumMode, key.Coercion);
            var resolve = (targetType == underlyingTarget ? EnumFastPath.ResolveMethod : EnumFastPath.ResolveNullableMethod)
                .MakeGenericMethod(underlyingTarget);
            valueExpression = Expression.Call(resolve, integer, Expression.Constant(fullConversion));
        }
        else if (key.RequiresCoercion)
        {
            if (!TryBuildDirectReadExpression(key, readerParam, targetType, underlyingTarget, out valueExpression))
            {
                // Fallback: one GetValue (DBNull is the null check), the coercer's boxed result, then unbox.
                var getValueMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;
                var stored = Expression.Variable(typeof(object), "value");
                var coercer = TypeCoercionHelper.ResolveCoercer(key.FieldType, targetType, key.EnumMode, key.Coercion);
                handlesNull = true;
                return Expression.Block(
                    new[] { stored },
                    Expression.Assign(stored, Expression.Call(readerParam, getValueMethod, Expression.Constant(key.Ordinal))),
                    Expression.IfThen(
                        Expression.Not(Expression.TypeIs(stored, typeof(DBNull))),
                        Expression.Assign(propertyAccess,
                            Expression.Convert(Expression.Invoke(Expression.Constant(coercer), stored), targetType))));
            }
        }
        else if (!key.FieldType.IsValueType && key.FieldType != typeof(string) && !targetType.IsValueType)
        {
            // A reference-type column read into a reference-type property (BitArray, char[], a spatial value): one
            // GetValue, whose DBNull is the null check.
            var stored = Expression.Variable(typeof(object), "value");
            handlesNull = true;
            return Expression.Block(
                new[] { stored },
                Expression.Assign(stored, Expression.Call(readerParam,
                    typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!, Expression.Constant(key.Ordinal))),
                Expression.IfThen(
                    Expression.Not(Expression.TypeIs(stored, typeof(DBNull))),
                    Expression.Assign(propertyAccess, Expression.Convert(stored, targetType))));
        }
        else
        {
            Expression rawValue;
            if (key.FieldType == typeof(object))
            {
                var getValueMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;
                rawValue = Expression.Call(readerParam, getValueMethod, Expression.Constant(key.Ordinal));
            }
            else if (key.FieldType == typeof(decimal))
            {
                // Sap.Data.Hana's GetValue returns its own HanaDecimal, so GetFieldValue<decimal> (a cast
                // of it) throws; GetDecimal returns the value (TYPE-002).
                var getDecimalMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetDecimal))!;
                rawValue = Expression.Call(readerParam, getDecimalMethod, Expression.Constant(key.Ordinal));
            }
            else if (TypedGetter(key.FieldType) is { } typedGetter)
            {
                // The provider's own getter for the column's type, as the gateway reads it: several
                // providers dispatch GetFieldValue<T> through a type switch, slow for a reference type
                // such as string (PERF-026).
                rawValue = Expression.Call(readerParam, typedGetter, Expression.Constant(key.Ordinal));
            }
            else
            {
                var getFieldValueMethod = _getFieldValueGenericMethod.MakeGenericMethod(key.FieldType);
                rawValue = Expression.Call(readerParam, getFieldValueMethod, Expression.Constant(key.Ordinal));
            }

            if (underlyingTarget == typeof(DateTime) && key.FieldType == typeof(DateTime))
            {
                // Unspecified reads as UTC, as on the gateway (DRY-003).
                Expression normalized = Expression.Call(ColumnConversion.NormalizeDateTime, rawValue);
                valueExpression = targetType != underlyingTarget ? Expression.Convert(normalized, targetType) : normalized;
            }
            else if (targetType == key.FieldType)
            {
                valueExpression = rawValue;
            }
            else if (NumericExpressions.Handles(key.FieldType, underlyingTarget))
            {
                var converted = NumericExpressions.Convert(rawValue, key.FieldType, underlyingTarget);
                valueExpression = targetType != underlyingTarget
                    ? Expression.Convert(converted, targetType)
                    : converted;
            }
            else
            {
                valueExpression = Expression.Convert(rawValue, targetType);
            }
        }

        return Expression.Assign(propertyAccess, valueExpression);
    }

    // The typed getter for the column's type, as the gateway reads it. Not Int64 (Informix.Net.Core's
    // GetInt64 rejects BIGSERIAL, which GetFieldValue<long> reads) nor decimal (read with GetDecimal
    // above for Sap.Data.Hana).
    private static MethodInfo? TypedGetter(Type fieldType) =>
        fieldType == typeof(long) || fieldType == typeof(decimal)
            ? null
            : ReaderGetters.TypedGetter(typeof(DbDataReader), fieldType);

    private static bool RequiresCoercion(Type fieldType, Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType.IsAssignableFrom(fieldType))
        {
            return false;
        }

        if (targetType.IsEnum)
        {
            return true;
        }

        if (targetType == typeof(object))
        {
            return false;
        }

        if (NumericTypes.IsNumeric(fieldType) && NumericTypes.IsNumeric(targetType))
        {
            return false;
        }

        if (targetType == typeof(bool) && NumericTypes.IsNumeric(fieldType))
        {
            return false;
        }

        return true;
    }

    private static Type ResolveFieldType(DbDataReader reader, int ordinal, Func<int, Type?>? unresolved = null)
    {
        try
        {
            // Null for a CLR type whose assembly isn't loaded (SqlClient on hierarchyid); the
            // dialect may still read it (TYPE-016), otherwise GetValue reports it.
            return reader.GetFieldType(ordinal) ?? unresolved?.Invoke(ordinal) ?? typeof(object);
        }
        catch (InvalidCastException)
        {
            // Npgsql throws for a type it has no handler for (PostGIS without NetTopologySuite);
            // the dialect may still read it (TYPE-002).
            return unresolved?.Invoke(ordinal) ?? typeof(object);
        }
        catch (InvalidOperationException)
        {
            return typeof(object);
        }
    }

    private static bool IsUnresolved(int ordinal, Func<int, Type?>? unresolved) =>
        unresolved?.Invoke(ordinal) is not null;

    // A column the provider reports no field type for can't use the compiled GetValue/GetFieldValue
    // setters; it is read by type through UnresolvedColumnReader, then coerced as usual (TYPE-016).
    private static Action<T, DbDataReader> CreateUnresolvedSetter<T>(PropertyInfo prop, Type fieldType,
        EnumParseFailureMode enumMode, int ordinal, TypeCoercionOptions? coercion,
        Func<IDataRecord, int, Type, object>? unresolvedReader)
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(fieldType, prop.PropertyType, enumMode, coercion);
        var read = unresolvedReader ?? UnresolvedColumnReader.Read;
        // A NULL leaves the property alone, as every other column path does: writing null stored a
        // struct property's default and overwrote initializers (REV-065).
        return (target, reader) =>
        {
            if (!reader.IsDBNull(ordinal))
            {
                prop.SetValue(target, coercer(read(reader, ordinal, fieldType)));
            }
        };
    }

    internal readonly record struct SetterCacheKey(
        Type TargetType,
        PropertyInfo Property,
        Type FieldType,
        bool RequiresCoercion,
        EnumParseFailureMode EnumMode,
        int Ordinal,
        TypeCoercionOptions? Coercion,
        bool NpgsqlTyped = false);

    private readonly record struct PropertyLookupCacheKey(Type Type, bool ColumnsOnly);

    // What one result-set column needs to be read into its property (and whether the dialect reads it itself).
    private sealed record ColumnBinding<T>(SetterCacheKey Key, Action<T, DbDataReader>? UnresolvedSetter,
        bool SkipNullCheck);

    private sealed record MapperPlan<T>(ColumnBinding<T>[] Columns, Func<DbDataReader, bool, T> RowMapper);

    private static IReadOnlyDictionary<string, PropertyInfo> GetPropertyLookup(Type type, IMapperOptions options)
    {
        var key = new PropertyLookupCacheKey(type, options.ColumnsOnly);
        return _propertyLookupCache.GetOrAdd(key, static cacheKey => BuildPropertyLookup(cacheKey));
    }

    private static IReadOnlyDictionary<string, PropertyInfo> BuildPropertyLookup(PropertyLookupCacheKey cacheKey)
    {
        var properties =
            cacheKey.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty);
        var comparer = StringComparer.OrdinalIgnoreCase;
        var lookup = new Dictionary<string, PropertyInfo>(properties.Length, comparer);

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            var setMethod = property.SetMethod;
            if (setMethod == null || !setMethod.IsPublic || setMethod.IsStatic)
            {
                continue;
            }

            string lookupKey;
            if (cacheKey.ColumnsOnly)
            {
                var column = property.GetCustomAttribute<ColumnAttribute>();
                if (column == null)
                {
                    continue;
                }

                lookupKey = column.Name;
            }
            else
            {
                lookupKey = property.Name;
            }

            if (!lookup.TryAdd(lookupKey, property))
            {
                throw new ArgumentException(
                    $"Duplicate column mapping detected for '{lookupKey}' on type '{cacheKey.Type.FullName}'.");
            }
        }

        return lookup;
    }

    /// <summary>
    /// Attempts to build a typed expression tree for known source→target coercion pairs,
    /// avoiding boxing of value-type return values compared to the
    /// <c>GetValue() + Func&lt;object?,object?&gt;</c> coercer path.
    /// </summary>
    /// <returns>
    /// <c>true</c> when a typed expression was built and <paramref name="valueExpression"/> is set;
    /// <c>false</c> when the pair is not handled here and the caller should fall back to the
    /// generic coercer path.
    /// </returns>
    // The conversions both mappers share (DRY-003): Guid bytes through the gateway's reader, and the
    // typed date, time and Guid coercions of TypedCoercions, read from the column's typed getter.
    private static bool TryBuildDirectReadExpression(
        SetterCacheKey key,
        ParameterExpression readerParam,
        Type targetType,
        Type underlyingTarget,
        out Expression valueExpression)
    {
        var ordinalConst = Expression.Constant(key.Ordinal);
        Expression? converted = null;
        if (key.FieldType == typeof(byte[]) && underlyingTarget == typeof(Guid))
        {
            // In the dialect's Guid byte order (SqlDialect.StoresGuidBytesBigEndian, TYPE-002); a reader
            // with no dialect uses .NET's mixed-endian order. Exactly 16 bytes (COR-003/COR-014).
            converted = Expression.Call(_readGuidFromBytesMethod, Expression.Convert(readerParam, typeof(IDataRecord)),
                ordinalConst, Expression.Constant(key.Coercion?.GuidBytesBigEndian ?? false));
        }
        else if (TypedCoercions.Find(key.FieldType, underlyingTarget) is { } typedCoercion)
        {
            var getter = TypedGetter(key.FieldType) ?? _getFieldValueGenericMethod.MakeGenericMethod(key.FieldType);
            converted = Expression.Call(typedCoercion, Expression.Call(readerParam, getter, ordinalConst),
                Expression.Constant(key.Coercion ?? TypeCoercionOptions.Default, typeof(TypeCoercionOptions)));
        }

        if (converted == null)
        {
            valueExpression = null!;
            return false;
        }

        valueExpression = targetType != underlyingTarget ? Expression.Convert(converted, targetType) : converted;
        return true;
    }

    private static readonly MethodInfo _readGuidFromBytesMethod = typeof(TypeCoercionHelper).GetMethod(
        nameof(TypeCoercionHelper.ReadGuidFromBytes), new[] { typeof(IDataRecord), typeof(int), typeof(bool) })!;

}
