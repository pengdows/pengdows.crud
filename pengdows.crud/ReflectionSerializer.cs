// =============================================================================
// FILE: ReflectionSerializer.cs
// PURPOSE: Lightweight reflection-based serializer/deserializer for converting
//          objects to/from dictionary representations without JSON dependencies.
//
// AI SUMMARY:
// - Provides Serialize() to convert any object to a Dictionary<string, object?>
//   representation (similar to what JSON would produce, but as CLR objects).
// - Provides Deserialize<T>() to reconstruct objects from dictionary data.
// - Handles:
//   * Simple types (primitives, enums, Guid) - passed through unchanged
//   * Strings - passed through unchanged
//   * Dictionaries - recursively serialized with string keys
//   * Collections/Arrays - recursively serialized as List<object?>
//   * Complex objects - serialized as Dictionary<string, object?> of properties
// - Use cases:
//   * Logging/diagnostics without JSON serialization overhead
//   * Configuration storage in non-JSON formats
//   * Testing and debugging object structures
// - Does not handle circular references or private fields.
// =============================================================================

using System.Collections;
using System.Reflection;

namespace pengdows.crud;

/// <summary>
/// Provides lightweight reflection-based serialization and deserialization
/// of objects to dictionary representations.
/// </summary>
/// <remarks>
/// <para>
/// This serializer converts objects to nested <see cref="Dictionary{TKey,TValue}"/>
/// structures, similar to what JSON serialization produces but without string
/// encoding. Useful for logging, debugging, or storing configuration.
/// </para>
/// <para>
/// <strong>Limitations:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>Does not detect or handle circular references</description></item>
/// <item><description>Only public readable properties are serialized</description></item>
/// <item><description>Only public writable properties are deserialized</description></item>
/// <item><description>Uses Activator.CreateInstance, requiring parameterless constructors</description></item>
/// </list>
/// </remarks>
public static class ReflectionSerializer
{
    /// <summary>
    /// Determines if a type is a simple/primitive type that should be passed through unchanged.
    /// </summary>
    private static bool IsSimpleType(Type type)
    {
        if (type.IsEnum)
        {
            return true;
        }

        var tc = Type.GetTypeCode(type);
        return tc != TypeCode.Object || type == typeof(Guid);
    }

    /// <summary>
    /// Serializes an object to a dictionary representation.
    /// </summary>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>
    /// A representation of the object:
    /// <list type="bullet">
    /// <item><description>null for null input</description></item>
    /// <item><description>The same value for simple types (primitives, enums, Guid, strings)</description></item>
    /// <item><description>A <see cref="Dictionary{TKey,TValue}"/> for dictionaries</description></item>
    /// <item><description>A <see cref="List{T}"/> for collections</description></item>
    /// <item><description>A <see cref="Dictionary{TKey,TValue}"/> with property names as keys for complex objects</description></item>
    /// </list>
    /// </returns>
    public static object? Serialize(object? obj)
    {
        if (obj == null)
        {
            return null;
        }

        var type = obj.GetType();
        if (IsSimpleType(type))
        {
            return obj;
        }

        if (obj is string)
        {
            return obj;
        }

        if (obj is IDictionary dict)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dict)
            {
                result[entry.Key.ToString() ?? string.Empty] = Serialize(entry.Value);
            }

            return result;
        }

        if (obj is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(Serialize(item));
            }

            return list;
        }

        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var objDict = new Dictionary<string, object?>();
        foreach (var prop in props)
        {
            if (!prop.CanRead)
            {
                continue;
            }

            objDict[prop.Name] = Serialize(prop.GetValue(obj));
        }

        return objDict;
    }

    /// <summary>
    /// Deserializes a dictionary representation back to an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target type. Must have a parameterless constructor.</typeparam>
    /// <param name="data">The dictionary data produced by <see cref="Serialize"/>.</param>
    /// <returns>A new instance of <typeparamref name="T"/> with properties populated from the data.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the data structure does not match the expected format for the target type.
    /// </exception>
    public static T Deserialize<T>(object? data) where T : new()
    {
        return (T)Deserialize(typeof(T), data)!;
    }

    private static object? Deserialize(Type targetType, object? data)
    {
        if (data == null)
        {
            return null;
        }

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

        if (typeof(string) == targetType)
        {
            return data.ToString();
        }

        if (typeof(IDictionary).IsAssignableFrom(targetType) && data is IDictionary<string, object?> dictData)
        {
            var dict = (IDictionary)Activator.CreateInstance(targetType)!;
            foreach (var kvp in dictData)
            {
                dict[kvp.Key] = Deserialize(targetType.GetGenericArguments()[1], kvp.Value);
            }

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
            {
                list.Add(Deserialize(elementType, item));
            }

            if (targetType.IsArray)
            {
                var array = Array.CreateInstance(elementType, list.Count);
                list.CopyTo(array, 0);
                return array;
            }

            return list;
        }

        if (data is not IDictionary<string, object?> objDict)
        {
            throw new InvalidOperationException($"Cannot deserialize type {targetType}");
        }

        var result = Activator.CreateInstance(targetType)!;
        foreach (var prop in targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanWrite)
            {
                continue;
            }

            if (objDict.TryGetValue(prop.Name, out var val))
            {
                var deserialized = Deserialize(prop.PropertyType, val);
                prop.SetValue(result, deserialized);
            }
        }

        return result;
    }
}