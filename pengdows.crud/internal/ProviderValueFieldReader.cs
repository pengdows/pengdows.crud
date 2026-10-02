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
    /// An array column read into a <paramref name="elementType"/>[] property: GetValue, or, when
    /// Npgsql refuses non-nullable elements (Spanner reports the field type only as System.Array), the
    /// nullable-element array of the property's element type.
    /// </summary>
    public static object ReadArray(IDataRecord record, int ordinal, Type elementType)
    {
        try
        {
            return record.GetValue(ordinal);
        }
        catch (InvalidCastException) when (TryReadNullableElementArray(record, ordinal, elementType, out var array))
        {
            return array;
        }
    }

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

    /// <summary>
    /// An array whose elements are a non-nullable value type (long[], int[], Guid[], ...), other than
    /// the binary and text buffers byte[]/char[].
    /// </summary>
    public static bool IsValueTypeArray(Type type) =>
        type.IsArray && type.GetElementType() is { IsValueType: true } element && Nullable.GetUnderlyingType(element) == null &&
        element != typeof(byte) && element != typeof(char);
}
