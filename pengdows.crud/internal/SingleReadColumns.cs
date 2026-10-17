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

namespace pengdows.crud.@internal;

internal static class SingleReadColumns
{
    internal static readonly MethodInfo StringFromValueMethod =
        typeof(SingleReadColumns).GetMethod(nameof(StringFromValue), BindingFlags.Public | BindingFlags.Static)!;

    internal static readonly MethodInfo BytesFromValueMethod =
        typeof(SingleReadColumns).GetMethod(nameof(BytesFromValue), BindingFlags.Public | BindingFlags.Static)!;

    public static string StringFromValue(object value, TypeCoercionOptions? options) =>
        value as string ?? (string)TypeCoercionHelper.Coerce(value, value.GetType(), typeof(string), options)!;

    public static byte[] BytesFromValue(object value, TypeCoercionOptions? options) =>
        value as byte[] ?? (byte[])TypeCoercionHelper.Coerce(value, value.GetType(), typeof(byte[]), options)!;
}
