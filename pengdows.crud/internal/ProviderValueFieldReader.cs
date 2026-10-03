// =============================================================================
// FILE: ProviderValueFieldReader.cs
// PURPOSE: Reads values a provider reports but can't return through GetValue.
//
// AI SUMMARY:
// - ReadGuid(): a Guid property's column through GetValue, or, when the provider can't return the
//   value (Npgsql on Spanner's uuid: no type name, GetValue throws), its 16 binary bytes in the
//   dialect's Guid byte order (confirmed live on the Spanner emulator, TYPE-002).
// - TryReadNullableElementArray(): an array of a value type that Npgsql refuses to read with
//   non-nullable elements ("returned array contains nulls", Spanner), read as T?[] and returned as
//   T[] when it holds no nulls (a null element then fails the property's conversion, loudly).
// - ReadArray<T>(): an array column into a T[] property, typed (bound once per plan): the provider's
//   T[] as is, a non-zero-based array (InterBase returns INTEGER [1:5] as Int32[*]) copied into a
//   zero-based one, or the nullable-element fallback above.
// - Used by CompiledMapperFactory, DataReaderMapper and TrackedReader.GetValue.
// =============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace pengdows.crud.@internal;

internal static class ProviderValueFieldReader
{
    private static readonly MethodInfo GetFieldValueDefinition = typeof(DbDataReader).GetMethods()
        .First(m => m.Name == nameof(DbDataReader.GetFieldValue) && m.IsGenericMethodDefinition);

    private static readonly ConcurrentDictionary<Type, MethodInfo> NullableArrayReaders = new();

    public static object ReadGuid(IDataRecord record, int ordinal, bool bigEndian)
    {
        try
        {
            return record.GetValue(ordinal);
        }
        catch (InvalidCastException)
        {
            var bytes = UnresolvedColumnReader.ReadBytes(record, ordinal);
            if (bytes.Length != 16)
            {
                throw;
            }

            return new Guid(bytes, bigEndian);
        }
    }

    /// <summary>
    /// An array column read into a <typeparamref name="T"/>[] property: the provider's T[] as is, a
    /// non-zero-based array copied into a zero-based one, or, when Npgsql refuses non-nullable elements
    /// (Spanner reports the field type only as System.Array), the nullable-element array. Anything else
    /// takes the general conversion, which fails loudly when it can't convert.
    /// </summary>
    public static T[] ReadArray<T>(IDataRecord record, int ordinal)
    {
        object value;
        try
        {
            value = record.GetValue(ordinal);
        }
        catch (InvalidCastException) when (TryReadNullableElementArray(record, ordinal, typeof(T), out var array))
        {
            value = array;
        }

        if (value is T[] typed)
        {
            return typed;
        }

        if (value is Array { Rank: 1 } other && other.GetType().GetElementType() == typeof(T))
        {
            var copy = new T[other.Length];
            Array.Copy(other, other.GetLowerBound(0), copy, 0, other.Length);
            return copy;
        }

        return (T[])TypeCoercionHelper.Coerce(value, value.GetType(), typeof(T[]))!;
    }

    /// <summary>
    /// A one-dimensional array property read through <see cref="ReadArray{T}"/> (not the binary and
    /// text buffers byte[]/char[]).
    /// </summary>
    public static bool IsReadableArray(Type type) =>
        type.IsArray && type.GetArrayRank() == 1 && type.GetElementType() is { } element &&
        element != typeof(byte) && element != typeof(char);

    /// <summary>
    /// A column the provider reports as an array (or as System.Array), not a binary or text buffer: a
    /// byte[] column (SingleStore's packed VECTOR) keeps its provider-specific conversion.
    /// </summary>
    public static bool IsArrayColumn(Type fieldType) =>
        fieldType == typeof(Array) || (fieldType.IsArray && fieldType != typeof(byte[]) && fieldType != typeof(char[]));

    /// <summary>An array column the driver returns as an Informix collection literal.</summary>
    public static T[] ReadCollectionLiteral<T>(IDataRecord record, int ordinal) =>
        CollectionLiteralFormat.Parse<T>(record.GetString(ordinal));

    internal static readonly MethodInfo ReadCollectionLiteralDefinition =
        typeof(ProviderValueFieldReader).GetMethod(nameof(ReadCollectionLiteral))!;

    internal static readonly MethodInfo ReadArrayDefinition =
        typeof(ProviderValueFieldReader).GetMethod(nameof(ReadArray))!;

    public static bool TryReadNullableElementArray(IDataRecord record, int ordinal, out object array)
    {
        array = null!;
        Type? fieldType;
        try
        {
            fieldType = (record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader)?.GetFieldType(ordinal);
        }
        catch (Exception)
        {
            return false;
        }

        var element = fieldType?.IsArray == true ? fieldType.GetElementType() : null;
        return element != null && TryReadNullableElementArray(record, ordinal, element, out array);
    }

    private static bool TryReadNullableElementArray(IDataRecord record, int ordinal, Type element, out object array)
    {
        array = null!;
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        if (reader == null || !element.IsValueType || Nullable.GetUnderlyingType(element) != null)
        {
            return false;
        }

        var nullableArray = (Array)NullableArrayReaders
            .GetOrAdd(element, static e => GetFieldValueDefinition.MakeGenericMethod(typeof(Nullable<>).MakeGenericType(e).MakeArrayType()))
            .Invoke(reader, new object[] { ordinal })!;

        var values = Array.CreateInstance(element, nullableArray.Length);
        for (var i = 0; i < nullableArray.Length; i++)
        {
            var item = nullableArray.GetValue(i);
            if (item == null)
            {
                array = nullableArray;
                return true;
            }

            values.SetValue(item, i);
        }

        array = values;
        return true;
    }
}
