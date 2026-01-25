#region

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.@internal;

#endregion

namespace pengdows.crud;

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

    internal DataReaderMapper()
    {
    }

    private readonly record struct PlanCacheKey(
        Type Type,
        string SchemaHash,
        bool ColumnsOnly,
        EnumParseFailureMode EnumMode);

    public static Task<List<T>> LoadObjectsFromDataReaderAsync<T>(
        IDataReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return Instance.LoadObjectsFromDataReaderAsync<T>(reader, cancellationToken);
    }

    public static Task<List<T>> LoadAsync<T>(
        IDataReader reader,
        MapperOptions options,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return Instance.LoadAsync<T>(reader, options, cancellationToken);
    }

    public static IAsyncEnumerable<T> StreamAsync<T>(
        IDataReader reader,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return Instance.StreamAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    public static IAsyncEnumerable<T> StreamAsync<T>(
        IDataReader reader,
        MapperOptions options,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return Instance.StreamAsync<T>(reader, options, cancellationToken);
    }

    Task<List<T>> IDataReaderMapper.LoadObjectsFromDataReaderAsync<T>(
        IDataReader reader,
        CancellationToken cancellationToken)
    {
        return LoadInternalAsync<T>(reader, MapperOptions.Default, cancellationToken);
    }

    Task<List<T>> IDataReaderMapper.LoadAsync<T>(
        IDataReader reader,
        MapperOptions options,
        CancellationToken cancellationToken)
    {
        return LoadInternalAsync<T>(reader, options, cancellationToken);
    }

    IAsyncEnumerable<T> IDataReaderMapper.StreamAsync<T>(
        IDataReader reader,
        MapperOptions options,
        CancellationToken cancellationToken)
    {
        return StreamInternalAsync<T>(reader, options, cancellationToken);
    }

    private async Task<List<T>> LoadInternalAsync<T>(
        IDataReader reader,
        MapperOptions options,
        CancellationToken cancellationToken)
        where T : class, new()
    {
        var result = new List<T>();
        await foreach (var item in StreamInternalAsync<T>(reader, options, cancellationToken))
        {
            result.Add(item);
        }

        return result;
    }

    private async IAsyncEnumerable<T> StreamInternalAsync<T>(
        IDataReader reader,
        MapperOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(reader);

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
            var obj = new T();
            for (var i = 0; i < plan.Ordinals.Length; i++)
            {
                var ordinal = plan.Ordinals[i];
                if (rdr.IsDBNull(ordinal))
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
                }
            }

            yield return obj;
        }
    }

    private static MapperPlan<T> BuildPlan<T>(DbDataReader reader, MapperOptions options)
    {
        var type = typeof(T);
        var propertyLookup = GetPropertyLookup(type, options);

        var ordinals = new List<int>();
        var setters = new List<Action<T, DbDataReader>>();
        var properties = new List<PropertyInfo>();

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
            }
        }

        return new MapperPlan<T>(
            ordinals.ToArray(),
            properties.ToArray(),
            setters.ToArray());
    }

    private static string BuildSchemaHash(DbDataReader reader, MapperOptions options)
    {
        var builder = SbLite.Create(stackalloc char[SbLite.DefaultStack]);

        // Include options in the hash to ensure proper cache invalidation
        builder.Append(options.ColumnsOnly ? '1' : '0');
        builder.Append('\u001F');
        builder.Append((int)options.EnumMode);
        builder.Append('\u001F');

        // Build schema with both field names and types
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            var name = reader.GetName(i);

            // Apply name policy if specified and not in ColumnsOnly mode
            if (!options.ColumnsOnly && options.NamePolicy != null)
            {
                name = options.NamePolicy(name);
            }

            builder.Append(name);
            builder.Append(':');

            // Include field type to ensure plans are rebuilt when column types change
            var fieldType = ResolveFieldType(reader, i);
            builder.Append(fieldType.AssemblyQualifiedName ?? fieldType.FullName ?? fieldType.Name);
        }

        return builder.ToString();
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

        Expression valueExpression;
        if (key.RequiresCoercion)
        {
            var getValueMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;
            var rawValue = Expression.Call(readerParam, getValueMethod, Expression.Constant(key.Ordinal));
            valueExpression = Expression.Convert(
                Expression.Call(
                    typeof(DataReaderMapper),
                    nameof(CoerceValue),
                    Type.EmptyTypes,
                    rawValue,
                    Expression.Constant(key.Property, typeof(PropertyInfo)),
                    Expression.Constant(key.FieldType, typeof(Type)),
                    Expression.Constant(key.EnumMode, typeof(EnumParseFailureMode))),
                key.Property.PropertyType);
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

            valueExpression = key.Property.PropertyType == key.FieldType
                ? rawValue
                : Expression.Convert(rawValue, key.Property.PropertyType);
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

        return true;
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
                    "Failed to coerce value '{Value}' to enum property {Property} of type {EnumType}.",
                    value,
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
        Action<T, DbDataReader>[] Setters);

    private static IReadOnlyDictionary<string, PropertyInfo> GetPropertyLookup(Type type, MapperOptions options)
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