using System;
using System.Collections;
using System.Reflection;

namespace pengdow.crud;

public static class ReflectionSerializer
{
    private static bool IsSimpleType(Type type)
    {
        if (type.IsEnum) return true;
        var tc = Type.GetTypeCode(type);
        return tc != TypeCode.Object || type == typeof(Guid);
    }

    public static object? Serialize(object? obj)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        if (IsSimpleType(type)) return obj;
        if (obj is string) return obj;
        if (obj is IDictionary dict)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dict)
                result[entry.Key.ToString() ?? string.Empty] = Serialize(entry.Value);
            return result;
        }
        if (obj is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable) list.Add(Serialize(item));
            return list;
        }

        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var objDict = new Dictionary<string, object?>();
        foreach (var prop in props)
        {
            if (!prop.CanRead) continue;
            objDict[prop.Name] = Serialize(prop.GetValue(obj));
        }
        return objDict;
    }

    public static T Deserialize<T>(object? data) where T : new()
    {
        return (T)Deserialize(typeof(T), data)!;
    }

    private static object? Deserialize(Type targetType, object? data)
    {
        if (data == null) return null;
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null)
        {
            // handle Nullable<T> by delegating to the underlying type
            return Deserialize(underlying, data);
        }
        if (IsSimpleType(targetType))
        {
            return Convert.ChangeType(data, Nullable.GetUnderlyingType(targetType) ?? targetType);
        }
        if (typeof(string) == targetType) return data.ToString();
        if (typeof(IDictionary).IsAssignableFrom(targetType) && data is IDictionary<string, object?> dictData)
        {
            var dict = (IDictionary)Activator.CreateInstance(targetType)!;
            foreach (var kvp in dictData)
                dict[kvp.Key] = Deserialize(targetType.GetGenericArguments()[1], kvp.Value);
            return dict;
        }
        if (typeof(IEnumerable).IsAssignableFrom(targetType) && data is IEnumerable enumerableData)
        {
            var elementType = targetType.IsArray
                ? targetType.GetElementType()!
                : targetType.GetGenericArguments()[0];
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var item in enumerableData)
                list.Add(Deserialize(elementType, item));
            if (targetType.IsArray)
            {
                var array = Array.CreateInstance(elementType, list.Count);
                list.CopyTo(array, 0);
                return array;
            }
            return list;
        }

        if (data is not IDictionary<string, object?> objDict)
            throw new InvalidOperationException($"Cannot deserialize type {targetType}");

        var result = Activator.CreateInstance(targetType)!;
        foreach (var prop in targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanWrite) continue;
            if (objDict.TryGetValue(prop.Name, out var val))
            {
                var deserialized = Deserialize(prop.PropertyType, val);
                prop.SetValue(result, deserialized);
            }
        }
        return result;
    }
}
