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

    internal static readonly MethodInfo TrimmedStringFromValueMethod =
        typeof(SingleReadColumns).GetMethod(nameof(TrimmedStringFromValue), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// The string read a statement uses for this provider: the trailing-NUL trim where the provider's text can end in padding
    /// (<see cref="pengdows.crud.dialects.DatabaseTraits.TextHasTrailingNul"/>), otherwise the plain read. Chosen when the plan is
    /// built, so a provider without the padding pays nothing per value.
    /// </summary>
    internal static MethodInfo StringReadFor(TypeCoercionOptions? options) =>
        options is { Provider: var provider } && pengdows.crud.dialects.DatabaseTraits.For(provider).TextHasTrailingNul
            ? TrimmedStringFromValueMethod
            : StringFromValueMethod;

    /// <summary>A text value that keeps no trailing padding NUL: the read of a provider whose text can end in one, which SQL text never does.</summary>
    public static string? TrimmedStringFromValue(object? value, TypeCoercionOptions? options)
    {
        // Converted once: a conversion may not be repeatable, and the trim must see the same text the read returns.
        var text = StringFromValue(value, options);
        return text is { Length: > 0 } && text[^1] == '\0' ? text.TrimEnd('\0') : text;
    }

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
