// =============================================================================
// FILE: SingleReadColumns.cs
// PURPOSE: Columns both mapper paths read with exactly one call into the reader.
//
// AI SUMMARY:
// - A reader getter does real work (a lookup, a decode), so each column costs what its reader calls cost.
// - String and byte[] columns are read with one GetValue; a DBNull result is the null check, so there is no
//   separate IsDBNull call and no second GetString/GetBytes.
// - Conversion goes through TypeCoercionHelper.Coerce, i.e. the CoercionRegistry coercion for the type and the
//   database (ByteArrayCoercion also takes ReadOnlyMemory, ArraySegment and Stream); a value that already is the
//   target type is used as it is.
// - Used by CompiledMapperFactory (TableGateway) and DataReaderMapper (arbitrary POCOs).
// =============================================================================

using System.Reflection;
using pengdows.crud.types.converters;

namespace pengdows.crud.@internal;

internal static class SingleReadColumns
{
    internal static readonly MethodInfo JsonTextFromValueMethod =
        typeof(SingleReadColumns).GetMethod(nameof(JsonTextFromValue))!;

    internal static readonly MethodInfo StringFromValueMethod =
        typeof(SingleReadColumns).GetMethod(nameof(StringFromValue), BindingFlags.Public | BindingFlags.Static)!;

    internal static readonly MethodInfo BytesFromValueMethod =
        typeof(SingleReadColumns).GetMethod(nameof(BytesFromValue), BindingFlags.Public | BindingFlags.Static)!;

    public static string? StringFromValue(object? value, TypeCoercionOptions? options) =>
        value switch
        {
            null => null,
            string text => text,
            _ => (string?)TypeCoercionHelper.Coerce(value, value.GetType(), typeof(string), options),
        };

    /// <summary>
    /// The JSON text of a JSON column's value: a BSON document (a dialect whose binary JSON is BSON) as its JSON, anything else
    /// as text.
    /// </summary>
    public static string? JsonTextFromValue(object value, TypeCoercionOptions? options) =>
        value is byte[] bytes && options is { BinaryJsonIsBson: true } && BsonJson.IsDocument(bytes)
            ? BsonJson.ToJson(bytes)
            : StringFromValue(value, options);

    public static byte[] BytesFromValue(object value, TypeCoercionOptions? options) =>
        value as byte[] ?? (byte[])TypeCoercionHelper.Coerce(value, value.GetType(), typeof(byte[]), options)!;
}
