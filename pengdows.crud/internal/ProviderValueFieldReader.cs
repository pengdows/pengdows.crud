// =============================================================================
// FILE: ProviderValueFieldReader.cs
// PURPOSE: Reads values a provider reports but can't return through GetValue.
//
// AI SUMMARY:
// - GuidColumnReader: a Guid property's column through GetValue, or, once the provider refuses it
//   (Npgsql on Spanner's uuid: no type name, GetValue throws), its 16 binary bytes in the dialect's
//   Guid byte order (confirmed live on the Spanner emulator, TYPE-002). One per plan column, so the
//   refusal is learned once rather than thrown on every row (PERF-010).
// - ValueArrayColumnReader<T>: the same learning for value-type arrays Npgsql on Spanner refuses.
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
using System.Linq.Expressions;
using System.Reflection;

namespace pengdows.crud.@internal;

internal static class ProviderValueFieldReader
{
    private static readonly MethodInfo GetFieldValueDefinition = ReaderGetters.GetFieldValueDefinition;

    private static readonly ConcurrentDictionary<Type, MethodInfo> NullableArrayReaders = new();

    /// <summary>
    /// A Guid column of one read plan (PERF-010): reads through GetValue until the provider refuses it
    /// once, then reads the bytes directly, instead of throwing and catching on every row.
    /// </summary>
    internal sealed class GuidColumnReader
    {
        private readonly bool _bigEndian;
        private volatile bool _readBytes;

        public GuidColumnReader(bool bigEndian) => _bigEndian = bigEndian;

        internal static readonly MethodInfo ReadMethod = typeof(GuidColumnReader).GetMethod(nameof(Read))!;

        public object Read(IDataRecord record, int ordinal)
        {
            if (_readBytes)
            {
                return record.IsDBNull(ordinal) ? DBNull.Value : TypeCoercionHelper.ReadGuidFromBytes(record, ordinal, _bigEndian);
            }

            try
            {
                return record.GetValue(ordinal);
            }
            catch (InvalidCastException)
            {
                var guid = TypeCoercionHelper.ReadGuidFromBytes(record, ordinal, _bigEndian);
                _readBytes = true;
                return guid;
            }
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

        return ToArray<T>(value);
    }

    private static T[] ToArray<T>(object value)
    {
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

    /// <summary>
    /// A value-type array column of one read plan (PERF-010). Npgsql on Spanner refuses GetValue for
    /// these even with no null elements (confirmed live), so after the first refusal the plan reads
    /// nullable elements directly instead of throwing and catching on every row.
    /// </summary>
    internal sealed class ValueArrayColumnReader<T> where T : struct
    {
        private volatile bool _readNullable;

        public T[] Read(IDataRecord record, int ordinal)
        {
            var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
            if (_readNullable && reader != null)
            {
                return ReadNullableElements(reader, ordinal);
            }

            object value;
            try
            {
                value = record.GetValue(ordinal);
            }
            catch (InvalidCastException) when (reader != null)
            {
                var array = ReadNullableElements(reader, ordinal);
                _readNullable = true;
                return array;
            }

            return ToArray<T>(value);
        }

        private static T[] ReadNullableElements(DbDataReader reader, int ordinal)
        {
            var nullable = reader.GetFieldValue<T?[]>(ordinal);
            var values = new T[nullable.Length];
            for (var i = 0; i < nullable.Length; i++)
            {
                if (nullable[i] is not { } item)
                {
                    // A null element fails the property's conversion, loudly.
                    return (T[])TypeCoercionHelper.Coerce(nullable, nullable.GetType(), typeof(T[]))!;
                }

                values[i] = item;
            }

            return values;
        }
    }

    /// <summary>
    /// The array reader one plan column binds: a learning <see cref="ValueArrayColumnReader{T}"/> for a
    /// non-nullable value-type element, otherwise <see cref="ReadArray{T}"/>.
    /// </summary>
    internal static Expression BindArrayRead(Type elementType, Expression record, Expression ordinal)
    {
        if (elementType.IsValueType && Nullable.GetUnderlyingType(elementType) == null)
        {
            var readerType = typeof(ValueArrayColumnReader<>).MakeGenericType(elementType);
            var instance = Activator.CreateInstance(readerType)!;
            return Expression.Call(Expression.Constant(instance), readerType.GetMethod("Read")!, record, ordinal);
        }

        return Expression.Call(ReadArrayDefinition.MakeGenericMethod(elementType), record, ordinal);
    }

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
