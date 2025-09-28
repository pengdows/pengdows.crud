#region

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using pengdows.crud.attributes;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud;

public sealed class DataReaderMapper : IDataReaderMapper
{
    public static readonly IDataReaderMapper Instance = new DataReaderMapper();

    private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>> _setterCache = new();
    private static readonly ConcurrentDictionary<PlanCacheKey, MapperPlan> _planCache = new();

    private readonly record struct PlanCacheKey(Type Type, string SchemaHash, bool ColumnsOnly, EnumParseFailureMode EnumMode);

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

        var plan = _planCache.GetOrAdd(planKey, _ => BuildPlan<T>(rdr, options));

        while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var obj = new T();
            for (var i = 0; i < plan.Ordinals.Length; i++)
            {
                var ordinal = plan.Ordinals[i];
                if (await rdr.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                try
                {
                    var raw = await rdr.GetFieldValueAsync<object>(ordinal, cancellationToken)
                        .ConfigureAwait(false);
                    var coerced = plan.Coercers[i](raw);
                    plan.Setters[i](obj, coerced);
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

    private static MapperPlan BuildPlan<T>(DbDataReader reader, MapperOptions options)
    {
        var type = typeof(T);
        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty);

        IEnumerable<PropertyInfo> candidates = props;
        if (options.ColumnsOnly)
        {
            candidates = candidates.Where(p => p.GetCustomAttribute<ColumnAttribute>() != null);
        }

        var propertyLookup = options.ColumnsOnly
            ? candidates.ToDictionary(
                p => p.GetCustomAttribute<ColumnAttribute>()!.Name,
                StringComparer.OrdinalIgnoreCase)
            : candidates.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var ordinals = new List<int>();
        var setters = new List<Action<object, object?>>();
        var coercers = new List<Func<object, object?>>();
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
                setters.Add(GetOrCreateSetter(prop));
                coercers.Add(value =>
                    Utils.IsNullOrDbNull(value)
                        ? null
                        : TypeCoercionHelper.Coerce(value, value.GetType(), prop.PropertyType));
                properties.Add(prop);
            }
        }

        return new MapperPlan(
            ordinals.ToArray(),
            properties.ToArray(),
            setters.ToArray(),
            coercers.ToArray());
    }

    private static string BuildSchemaHash(DbDataReader reader, MapperOptions options)
    {
        var normalizedNames = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);

            if (!options.ColumnsOnly && options.NamePolicy != null)
            {
                name = options.NamePolicy(name);
            }

            normalizedNames[i] = name;
        }

        var builder = new StringBuilder();
        builder.Append(options.ColumnsOnly ? '1' : '0');
        builder.Append('\u001F');
        builder.Append((int)options.EnumMode);
        builder.Append('\u001F');
        builder.AppendJoin('\u001F', normalizedNames);

        return builder.ToString();
    }

    private static Action<object, object?> GetOrCreateSetter(PropertyInfo prop)
    {
        return _setterCache.GetOrAdd(prop, p =>
        {
            var objParam = Expression.Parameter(typeof(object));
            var valueParam = Expression.Parameter(typeof(object));

            var castObj = Expression.Convert(objParam, p.DeclaringType!);
            var castValue = Expression.Convert(valueParam, p.PropertyType);

            var propertyAccess = Expression.Property(castObj, p);
            var assignment = Expression.Assign(propertyAccess, castValue);

            var lambda = Expression.Lambda<Action<object, object?>>(assignment, objParam, valueParam);
            return lambda.Compile();
        });
    }

    private sealed record MapperPlan(
        int[] Ordinals,
        PropertyInfo[] Properties,
        Action<object, object?>[] Setters,
        Func<object, object?>[] Coercers);
}

