// =============================================================================
// FILE: SequenceCoercion.cs
// PURPOSE: Converts collections element by element: an array or list from another collection, from numeric JSON text, or
//          from a provider vector value.
//
// AI SUMMARY:
// - TryCoerceSequence: the entry point; the target's element type picks a builder (cached per element type).
// - BuildSequence<T>: one pass over the source items, each converted with CoerceSequenceElement<T>, into an array or list.
// - ParseNumericJsonArray: the elements of a numeric JSON array text.
// - A provider vector (SqlVector, Pgvector) is unwrapped first (ProviderVectorValues).
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using pengdows.crud.@internal;
using pengdows.crud.enums;

namespace pengdows.crud.types.coercion;

internal static class SequenceCoercion
{
    internal static bool TryCoerceSequence(object value, Type target, TypeCoercionOptions options, out object? result)
    {
        result = null;
        Type? elementType = target.IsArray ? target.GetElementType()
            : target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>) ? target.GetGenericArguments()[0]
            : null;
        if (elementType == null)
        {
            return false;
        }

        // TYPE-015: vector columns. SQL Server's VECTOR reads as "[1.5000000e+000,...]" through
        // SqlClient 6.0 and as SqlVector<T> (Memory) through 6.1+; the Pgvector.Npgsql plugin reads
        // pgvector as Pgvector.Vector (ToArray()). Each becomes an array, coerced element-wise below.
        if (value is string literal && options.ReadsCollectionLiterals && target.IsArray)
        {
            // The dialect returns collections as literal text (Informix LIST/SET/MULTISET).
            try
            {
                result = CollectionLiteralParse.MakeGenericMethod(elementType).Invoke(null, new object[] { literal });
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Surface the parser's own FormatException, not the reflection wrapper.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            return true;
        }

        if (value is string text)
        {
            if (!NumericTypes.IsNumeric(Nullable.GetUnderlyingType(elementType) ?? elementType))
            {
                return false;
            }

            value = ParseNumericJsonArray(text);
        }
        else if (TryUnwrapProviderVector(value, out var unwrapped))
        {
            value = unwrapped;
        }

        if (value is not System.Collections.IEnumerable items)
        {
            return false;
        }

        result = SequenceBuilders.GetOrAdd(elementType, CreateSequenceBuilder)(items, options, target.IsArray);
        return true;
    }

    // PERF-011: one typed builder per element type. The general path built a List through Activator,
    // added boxed elements through IList, copied the list into an array and coerced elements already
    // of the target type (a 1536-dimension embedding: about 240 us and 133 KB).
    internal delegate object SequenceBuilder(System.Collections.IEnumerable items, TypeCoercionOptions options, bool asArray);

    private static readonly ConcurrentDictionary<Type, SequenceBuilder> SequenceBuilders = new();

    private static SequenceBuilder CreateSequenceBuilder(Type elementType) =>
        (SequenceBuilder)typeof(SequenceCoercion)
            .GetMethod(nameof(BuildSequence), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(elementType)
            .CreateDelegate(typeof(SequenceBuilder));

    private static object BuildSequence<T>(System.Collections.IEnumerable items, TypeCoercionOptions options, bool asArray)
    {
        // Elements already of the target type are what Coerce would return, except DateTime and
        // DateTimeOffset (normalized) and object (DBNull becomes null).
        var underlying = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var copyAsIs = items is IEnumerable<T> && typeof(T) != typeof(object) &&
                       underlying != typeof(DateTime) && underlying != typeof(DateTimeOffset);

        if (asArray && items is System.Collections.ICollection collection)
        {
            var array = new T[collection.Count];
            var i = 0;
            if (copyAsIs)
            {
                foreach (var item in (IEnumerable<T>)items)
                {
                    array[i++] = item;
                }
            }
            else
            {
                foreach (var item in items)
                {
                    array[i++] = CoerceSequenceElement<T>(item, options);
                }
            }

            return array;
        }

        var list = items is System.Collections.ICollection sized ? new List<T>(sized.Count) : new List<T>();
        if (copyAsIs)
        {
            list.AddRange((IEnumerable<T>)items);
        }
        else
        {
            foreach (var item in items)
            {
                list.Add(CoerceSequenceElement<T>(item, options));
            }
        }

        return asArray ? list.ToArray() : list;
    }

    private static T CoerceSequenceElement<T>(object? item, TypeCoercionOptions options)
    {
        if (item is null || item is DBNull)
        {
            // As IList.Add(null) on a List of a non-nullable value type did.
            return default(T) is null
                ? default!
                : throw new InvalidCastException(
                    $"A NULL element cannot be stored in a non-nullable {typeof(T).Name} collection.");
        }

        return (T)TypeCoercionHelper.Coerce(item, item.GetType(), typeof(T), options)!;
    }

    // Numbers kept as their invariant text, so each element parses straight into its target type
    // (a float gets the nearest float to the printed value, not a double rounded again).
    private static readonly System.Reflection.MethodInfo CollectionLiteralParse =
        typeof(CollectionLiteralFormat).GetMethod(nameof(CollectionLiteralFormat.Parse))!;

    private static string[] ParseNumericJsonArray(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            // A conversion failure like any other, so hydration reports DataMappingException (REV-034).
            throw new FormatException("Expected a JSON array of numbers.", ex);
        }

        using var _ = document;
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Expected a JSON array of numbers.");
        }

        var elements = new string[document.RootElement.GetArrayLength()];
        var i = 0;
        foreach (var element in document.RootElement.EnumerateArray())
        {
            elements[i++] = element.ValueKind == JsonValueKind.Number
                ? element.GetRawText()
                : throw new FormatException($"Expected a number but found {element.ValueKind}.");
        }

        return elements;
    }

    private static bool TryUnwrapProviderVector(object value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? array) =>
        ProviderVectorValues.TryUnwrap(value, out array);


}
