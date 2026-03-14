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
//   * Caches compiled setters per (Type, PropertyInfo) tuple
//   * Bounded LRU caches prevent unbounded memory growth
//   * BuildSchemaHash returns a long computed via pure arithmetic — zero string
//     or AssemblyQualifiedName allocations on the hot path
//   * LoadInternalAsync uses a direct while(ReadAsync) loop with the plan
//     hoisted outside; per-row mapping is a simple delegate call (MapSingleRow)
// - Column matching:
//   * By default matches columns to properties by name (case-insensitive)
//   * With ColumnsOnly=true, only maps [Column]-attributed properties
// - Type coercion: Uses TypeCoercionHelper for automatic type conversion.
// - Enum handling: Configurable via MapperOptions.EnumParseFailureMode.
// - Null handling: Nullable properties receive null; non-nullable get defaults.
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
    // These are LRU-ish bounded caches that evict oldest entries when capacity is exceeded.
    private const int MaxPlanCacheSize = 128;
    private const int MaxSetterCacheSize = 512;
    private const int MaxPropertyLookupCacheSize = 64;

    private static readonly BoundedCache<SetterCacheKey, Delegate> _setterCache = new(MaxSetterCacheSize);
    private static readonly BoundedCache<PlanCacheKey, object> _planCache = new(MaxPlanCacheSize);

    private static readonly BoundedCache<PropertyLookupCacheKey, IReadOnlyDictionary<string, PropertyInfo>>
        _propertyLookupCache = new(MaxPropertyLookupCacheSize);

    private static readonly MethodInfo _getFieldValueGenericMethod = ResolveGetFieldValueMethod();

    // Cached MethodInfo/ConstructorInfo for typed direct-read expression trees.
    // Resolved once at class load to avoid per-plan reflection overhead.
    // NormalizeDateTime: treats Unspecified as UTC, converts Local to UTC.
    // Used after GetDateTime() to apply the same UTC policy that other paths use.
    private static readonly MethodInfo _normalizeDateTimeMethod =
        typeof(TypeCoercionHelper).GetMethod(
            nameof(TypeCoercionHelper.NormalizeDateTime),
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(DateTime) },
            null)!;

    // IDataRecord.GetDateTime(int) — called directly for string→DateTime coercion to
    // delegate parsing to the DB driver (single parse, no intermediate string allocation).
    private static readonly MethodInfo _getDateTimeMethod =
        typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetDateTime), new[] { typeof(int) })!;

    private static readonly MethodInfo _coerceDateTimeOffsetFromStringMethod =
        typeof(TypeCoercionHelper).GetMethod(
            nameof(TypeCoercionHelper.CoerceDateTimeOffsetFromString),
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(string) },
            null)!;

    private static readonly MethodInfo _coerceDateTimeOffsetFromDateTimeMethod =
        typeof(TypeCoercionHelper).GetMethod(
            nameof(TypeCoercionHelper.CoerceDateTimeOffsetFromDateTime),
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(DateTime) },
            null)!;

    private static readonly MethodInfo _guidParseMethod =
        typeof(Guid).GetMethod(nameof(Guid.Parse), new[] { typeof(string) })!;

    private static readonly ConstructorInfo _guidFromBytesConstructor =
        typeof(Guid).GetConstructor(new[] { typeof(byte[]) })!;

    internal DataReaderMapper()
    {
    }

    private readonly record struct PlanCacheKey(
        Type Type,
        long SchemaHash,
        bool ColumnsOnly,
        EnumParseFailureMode EnumMode);

    public static Task<List<T>> LoadObjectsFromDataReaderAsync<T>(
        ITrackedReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return LoadInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    public static Task<List<T>> LoadAsync<T>(
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

    internal static Task<List<T>> LoadObjectsFromDataReaderAsync<T>(
        IDataReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return LoadInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    internal static Task<List<T>> LoadAsync<T>(
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

    Task<List<T>> IDataReaderMapper.LoadAsync<T>(
        ITrackedReader reader,
        CancellationToken cancellationToken)
    {
        return LoadInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    Task<List<T>> IDataReaderMapper.LoadAsync<T>(
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

    private static async Task<List<T>> LoadInternalAsync<T>(
        ITrackedReader reader,
        IMapperOptions options,
        CancellationToken cancellationToken)
        where T : class, new()
    {
        var recordReader = GetRecordReader(reader);
        options ??= MapperOptions.Default;

        var schemaHash = BuildSchemaHash(recordReader, options);
        var planKey = new PlanCacheKey(typeof(T), schemaHash, options.ColumnsOnly, options.EnumMode);
        var plan = (MapperPlan<T>)_planCache.GetOrAdd(planKey, _ => BuildPlan<T>(recordReader, options));

        var result = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(MapSingleRow(recordReader, plan, options));
        }

        return result;
    }

    private static async Task<List<T>> LoadInternalAsync<T>(
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

        var schemaHash = BuildSchemaHash(rdr, options);
        var planKey = new PlanCacheKey(typeof(T), schemaHash, options.ColumnsOnly, options.EnumMode);
        var plan = (MapperPlan<T>)_planCache.GetOrAdd(planKey, _ => BuildPlan<T>(rdr, options));

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
        var recordReader = GetRecordReader(reader);
        options ??= MapperOptions.Default;

        var schemaHash = BuildSchemaHash(recordReader, options);
        var planKey = new PlanCacheKey(typeof(T), schemaHash, options.ColumnsOnly, options.EnumMode);
        var plan = (MapperPlan<T>)_planCache.GetOrAdd(planKey, _ => BuildPlan<T>(recordReader, options));

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

        var schemaHash = BuildSchemaHash(rdr, options);
        var planKey = new PlanCacheKey(typeof(T), schemaHash, options.ColumnsOnly, options.EnumMode);

        var plan = (MapperPlan<T>)_planCache.GetOrAdd(planKey, _ => BuildPlan<T>(rdr, options));

        while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return MapSingleRow(rdr, plan, options);
        }
    }

    private static DbDataReader GetRecordReader(ITrackedReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader is IInternalTrackedReader internalReader)
        {
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
        var obj = new T();
        for (var i = 0; i < plan.Ordinals.Length; i++)
        {
            var ordinal = plan.Ordinals[i];
            if (!plan.SkipNullCheck[i] && rdr.IsDBNull(ordinal))
            {
                continue;
            }

            try
            {
                plan.Setters[i](obj, rdr);
            }
            catch (Exception ex)
            {
                if (options.Strict)
                {
                    throw new InvalidOperationException(
                        $"Failed to map column '{rdr.GetName(ordinal)}' to property '{plan.Properties[i].Name}'.",
                        ex);
                }

                TypeCoercionHelper.Logger.LogWarning(
                    ex,
                    "Failed to map column '{Column}' to property '{Property}'.",
                    rdr.GetName(ordinal),
                    plan.Properties[i].Name);
            }
        }

        return obj;
    }

    private static MapperPlan<T> BuildPlan<T>(DbDataReader reader, IMapperOptions options)
    {
        var type = typeof(T);
        var propertyLookup = GetPropertyLookup(type, options);

        var ordinals = new List<int>();
        var setters = new List<Action<T, DbDataReader>>();
        var properties = new List<PropertyInfo>();
        var skipNullChecks = new List<bool>();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);

            if (!options.ColumnsOnly && options.NamePolicy != null)
            {
                name = options.NamePolicy(name);
            }

            if (propertyLookup.TryGetValue(name, out var prop))
            {
                ordinals.Add(i);
                var fieldType = ResolveFieldType(reader, i);
                var requiresCoercion = RequiresCoercion(fieldType, prop.PropertyType);
                setters.Add(GetOrCreateSetter<T>(prop, fieldType, requiresCoercion, options.EnumMode, i));
                properties.Add(prop);
                // Non-nullable value types cannot hold null at the .NET level. Skip the IsDBNull
                // guard for these columns — if the DB returns NULL for a non-nullable property,
                // the typed getter will throw, which is the correct loud failure.
                var propType = prop.PropertyType;
                skipNullChecks.Add(propType.IsValueType && Nullable.GetUnderlyingType(propType) == null);
            }
        }

        return new MapperPlan<T>(
            ordinals.ToArray(),
            properties.ToArray(),
            setters.ToArray(),
            skipNullChecks.ToArray());
    }

    /// <summary>
    /// Computes a long hash over the reader schema and mapping options.
    /// Pure arithmetic — no string allocations (AssemblyQualifiedName is gone).
    /// </summary>
    private static long BuildSchemaHash(DbDataReader reader, IMapperOptions options)
    {
        var hash = reader.FieldCount * 397L;
        hash = unchecked(hash * 31L + (options.ColumnsOnly ? 1 : 0));
        hash = unchecked(hash * 31L + (int)options.EnumMode);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);

            if (!options.ColumnsOnly && options.NamePolicy != null)
            {
                name = options.NamePolicy(name);
            }

            hash = unchecked(hash * 31L + StringComparer.OrdinalIgnoreCase.GetHashCode(name));

            var fieldType = ResolveFieldType(reader, i);
            hash = unchecked(hash * 31L + fieldType.GetHashCode());
        }

        return hash;
    }

    private static Action<T, DbDataReader> GetOrCreateSetter<T>(
        PropertyInfo prop,
        Type fieldType,
        bool requiresCoercion,
        EnumParseFailureMode enumMode,
        int ordinal)
    {
        var key = new SetterCacheKey(typeof(T), prop, fieldType, requiresCoercion, enumMode, ordinal);
        return (Action<T, DbDataReader>)_setterCache.GetOrAdd(key, static k => CompileSetter<T>(k));
    }

    private static Action<T, DbDataReader> CompileSetter<T>(SetterCacheKey key)
    {
        var objParam = Expression.Parameter(typeof(T), "target");
        var readerParam = Expression.Parameter(typeof(DbDataReader), "reader");

        var propertyAccess = Expression.Property(objParam, key.Property);
        var targetType = key.Property.PropertyType;
        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        Expression valueExpression;
        if (key.RequiresCoercion)
        {
            if (!TryBuildDirectReadExpression(key, readerParam, targetType, underlyingTarget, out valueExpression))
            {
                // Fallback: GetValue() returns boxed object; coercer returns boxed result; then unbox.
                var getValueMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;
                var rawValue = Expression.Call(readerParam, getValueMethod, Expression.Constant(key.Ordinal));
                var coercer = TypeCoercionHelper.ResolveCoercer(key.FieldType, targetType, key.EnumMode);
                var invokeCoercer = Expression.Invoke(Expression.Constant(coercer), rawValue);
                valueExpression = Expression.Convert(invokeCoercer, targetType);
            }
        }
        else
        {
            Expression rawValue;
            if (key.FieldType == typeof(object))
            {
                var getValueMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;
                rawValue = Expression.Call(readerParam, getValueMethod, Expression.Constant(key.Ordinal));
            }
            else
            {
                var getFieldValueMethod = _getFieldValueGenericMethod.MakeGenericMethod(key.FieldType);
                rawValue = Expression.Call(readerParam, getFieldValueMethod, Expression.Constant(key.Ordinal));
            }

            if (targetType == key.FieldType)
            {
                valueExpression = rawValue;
            }
            else if (underlyingTarget == typeof(bool) && IsNumericType(key.FieldType))
            {
                var nonZero = Expression.NotEqual(rawValue, Expression.Default(key.FieldType));
                valueExpression = targetType != underlyingTarget
                    ? Expression.Convert(nonZero, targetType)
                    : nonZero;
            }
            else if (IsNumericType(key.FieldType) && IsNumericType(underlyingTarget))
            {
                var converted = BuildNumericConversion(rawValue, key.FieldType, underlyingTarget);
                valueExpression = targetType != underlyingTarget
                    ? Expression.Convert(converted, targetType)
                    : converted;
            }
            else
            {
                valueExpression = Expression.Convert(rawValue, targetType);
            }
        }

        var assignment = Expression.Assign(propertyAccess, valueExpression);
        var lambda = Expression.Lambda<Action<T, DbDataReader>>(assignment, objParam, readerParam);
        return lambda.Compile();
    }

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

        if (IsNumericType(fieldType) && IsNumericType(targetType))
        {
            return false;
        }

        if (targetType == typeof(bool) && IsNumericType(fieldType))
        {
            return false;
        }

        return true;
    }

    private static Expression BuildNumericConversion(Expression rawValue, Type sourceType, Type targetType)
    {
        if (sourceType == targetType)
        {
            return rawValue;
        }

        // Integral → integral narrowing (e.g. long → int, long → short, int → byte).
        // Use unchecked conversion — in a DB context the schema is trusted to fit.
        // Emits conv.i4 / conv.i2 / etc. (1 CPU instruction, no overflow branch).
        if (IsIntegralType(sourceType) && IsIntegralType(targetType))
        {
            return Expression.Convert(rawValue, targetType);
        }

        // float/double/decimal → integral: use Convert.ToXxx for rounding semantics.
        if (IsIntegralType(targetType) && !IsIntegralType(sourceType))
        {
            var method = ResolveConvertMethod(targetType, sourceType);
            if (method != null)
            {
                return Expression.Call(method, rawValue);
            }
        }

        if (targetType == typeof(decimal) && (sourceType == typeof(double) || sourceType == typeof(float)))
        {
            var method = ResolveConvertMethod(targetType, sourceType);
            if (method != null)
            {
                return Expression.Call(method, rawValue);
            }
        }

        if ((targetType == typeof(double) || targetType == typeof(float)) && sourceType == typeof(decimal))
        {
            var method = ResolveConvertMethod(targetType, sourceType);
            if (method != null)
            {
                return Expression.Call(method, rawValue);
            }
        }

        return Expression.ConvertChecked(rawValue, targetType);
    }

    private static MethodInfo? ResolveConvertMethod(Type targetType, Type sourceType)
    {
        var methodName = targetType switch
        {
            var t when t == typeof(byte) => nameof(Convert.ToByte),
            var t when t == typeof(sbyte) => nameof(Convert.ToSByte),
            var t when t == typeof(short) => nameof(Convert.ToInt16),
            var t when t == typeof(ushort) => nameof(Convert.ToUInt16),
            var t when t == typeof(int) => nameof(Convert.ToInt32),
            var t when t == typeof(uint) => nameof(Convert.ToUInt32),
            var t when t == typeof(long) => nameof(Convert.ToInt64),
            var t when t == typeof(ulong) => nameof(Convert.ToUInt64),
            var t when t == typeof(float) => nameof(Convert.ToSingle),
            var t when t == typeof(double) => nameof(Convert.ToDouble),
            var t when t == typeof(decimal) => nameof(Convert.ToDecimal),
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(methodName))
        {
            return null;
        }

        return typeof(Convert).GetMethod(methodName, new[] { sourceType });
    }

    private static bool IsNumericType(Type type)
    {
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte => true,
            TypeCode.SByte => true,
            TypeCode.Int16 => true,
            TypeCode.UInt16 => true,
            TypeCode.Int32 => true,
            TypeCode.UInt32 => true,
            TypeCode.Int64 => true,
            TypeCode.UInt64 => true,
            TypeCode.Single => true,
            TypeCode.Double => true,
            TypeCode.Decimal => true,
            _ => false
        };
    }

    private static bool IsIntegralType(Type type)
    {
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte => true,
            TypeCode.SByte => true,
            TypeCode.Int16 => true,
            TypeCode.UInt16 => true,
            TypeCode.Int32 => true,
            TypeCode.UInt32 => true,
            TypeCode.Int64 => true,
            TypeCode.UInt64 => true,
            _ => false
        };
    }

    private static Type ResolveFieldType(DbDataReader reader, int ordinal)
    {
        try
        {
            return reader.GetFieldType(ordinal);
        }
        catch (InvalidOperationException)
        {
            return typeof(object);
        }
    }

    private static object? CoerceValue(
        object? value,
        PropertyInfo property,
        Type fieldType,
        EnumParseFailureMode enumMode)
    {
        if (Utils.IsNullOrDbNull(value))
        {
            return null;
        }

        try
        {
            return TypeCoercionHelper.Coerce(value!, fieldType, property.PropertyType);
        }
        catch (Exception ex) when (TryHandleEnumFailure(value!, property, enumMode, ex, out var handled))
        {
            return handled;
        }
    }

    private static bool TryHandleEnumFailure(
        object value,
        PropertyInfo property,
        EnumParseFailureMode enumMode,
        Exception exception,
        out object? result)
    {
        var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (!enumType.IsEnum)
        {
            result = default;
            return false;
        }

        if (enumMode == EnumParseFailureMode.Throw)
        {
            result = default;
            return false;
        }

        switch (enumMode)
        {
            case EnumParseFailureMode.SetNullAndLog:
                TypeCoercionHelper.Logger.LogWarning(
                    exception,
                    "Failed to coerce value to enum property {Property} of type {EnumType}.",
                    property.Name,
                    enumType);
                if (Nullable.GetUnderlyingType(property.PropertyType) != null)
                {
                    result = null;
                    return true;
                }

                var fallback = Activator.CreateInstance(Enum.GetUnderlyingType(enumType))!;
                result = Enum.ToObject(enumType, fallback);
                return true;
            case EnumParseFailureMode.SetDefaultValue:
                if (Nullable.GetUnderlyingType(property.PropertyType) != null)
                {
                    result = null;
                    return true;
                }

                var defaultValue = Activator.CreateInstance(Enum.GetUnderlyingType(enumType))!;
                result = Enum.ToObject(enumType, defaultValue);
                return true;
            default:
                result = null;
                return false;
        }
    }

    private readonly record struct SetterCacheKey(
        Type TargetType,
        PropertyInfo Property,
        Type FieldType,
        bool RequiresCoercion,
        EnumParseFailureMode EnumMode,
        int Ordinal);

    private readonly record struct PropertyLookupCacheKey(Type Type, bool ColumnsOnly);

    private sealed record MapperPlan<T>(
        int[] Ordinals,
        PropertyInfo[] Properties,
        Action<T, DbDataReader>[] Setters,
        bool[] SkipNullCheck);

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
    private static bool TryBuildDirectReadExpression(
        SetterCacheKey key,
        ParameterExpression readerParam,
        Type targetType,
        Type underlyingTarget,
        out Expression valueExpression)
    {
        var fieldType = key.FieldType;
        var ordinalConst = Expression.Constant(key.Ordinal);
        var isNullable = targetType != underlyingTarget; // targetType is Nullable<underlyingTarget>

        // string → DateTime / DateTime?
        // Use reader.GetDateTime(ordinal) directly rather than GetFieldValue<string>() +
        // CoerceDateTimeFromString(). Delegating to the DB driver:
        //   • avoids allocating an intermediate C# string
        //   • uses a single DateTime.Parse call (no DateTimeOffset.TryParse first attempt)
        //   • matches what Dapper emits and what CompiledMapperFactory does for drivers
        //     that natively return typeof(DateTime) from GetFieldType()
        // NormalizeDateTime then applies the same UTC policy as every other path.
        if (fieldType == typeof(string) && underlyingTarget == typeof(DateTime))
        {
            var rawDt = Expression.Call(readerParam, _getDateTimeMethod, ordinalConst);
            var normalized = Expression.Call(_normalizeDateTimeMethod, rawDt);
            valueExpression = isNullable ? Expression.Convert(normalized, targetType) : normalized;
            return true;
        }

        // string → DateTimeOffset / DateTimeOffset?
        if (fieldType == typeof(string) && underlyingTarget == typeof(DateTimeOffset))
        {
            var getStr = _getFieldValueGenericMethod.MakeGenericMethod(typeof(string));
            var rawStr = Expression.Call(readerParam, getStr, ordinalConst);
            var parsed = Expression.Call(_coerceDateTimeOffsetFromStringMethod, rawStr);
            valueExpression = isNullable ? Expression.Convert(parsed, targetType) : parsed;
            return true;
        }

        // DateTime → DateTimeOffset / DateTimeOffset?
        if (fieldType == typeof(DateTime) && underlyingTarget == typeof(DateTimeOffset))
        {
            var getDateTime = _getFieldValueGenericMethod.MakeGenericMethod(typeof(DateTime));
            var rawDt = Expression.Call(readerParam, getDateTime, ordinalConst);
            var converted = Expression.Call(_coerceDateTimeOffsetFromDateTimeMethod, rawDt);
            valueExpression = isNullable ? Expression.Convert(converted, targetType) : converted;
            return true;
        }

        // string → Guid / Guid?
        if (fieldType == typeof(string) && underlyingTarget == typeof(Guid))
        {
            var getStr = _getFieldValueGenericMethod.MakeGenericMethod(typeof(string));
            var rawStr = Expression.Call(readerParam, getStr, ordinalConst);
            var parsed = Expression.Call(_guidParseMethod, rawStr);
            valueExpression = isNullable ? Expression.Convert(parsed, targetType) : parsed;
            return true;
        }

        // byte[] → Guid / Guid?
        // NOTE: new Guid(byte[]) uses .NET's mixed-endian layout (Data1/2/3 little-endian).
        // This is correct when the driver returns raw bytes from a column that was written
        // with the base SqlDialect.SerializeGuidAsBinary (also mixed-endian).
        // If a dialect overrides SerializeGuidAsBinary to write big-endian bytes (e.g. Firebird),
        // its driver MUST return a native Guid for the column — not byte[] — so this path is
        // never reached and the byte-order mismatch never occurs.
        if (fieldType == typeof(byte[]) && underlyingTarget == typeof(Guid))
        {
            var getBytes = _getFieldValueGenericMethod.MakeGenericMethod(typeof(byte[]));
            var rawBytes = Expression.Call(readerParam, getBytes, ordinalConst);
            var newGuid = Expression.New(_guidFromBytesConstructor, rawBytes);
            valueExpression = isNullable ? Expression.Convert(newGuid, targetType) : newGuid;
            return true;
        }

        valueExpression = null!;
        return false;
    }

    private static MethodInfo ResolveGetFieldValueMethod()
    {
        var methods = typeof(DbDataReader).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            if (method.IsGenericMethodDefinition && method.Name == nameof(DbDataReader.GetFieldValue))
            {
                return method;
            }
        }

        throw new InvalidOperationException("DbDataReader.GetFieldValue<T> method not found.");
    }
}
