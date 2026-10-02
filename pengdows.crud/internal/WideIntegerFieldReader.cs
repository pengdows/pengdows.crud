// =============================================================================
// FILE: WideIntegerFieldReader.cs
// PURPOSE: Reads an integer column whose value is beyond Int64 although the provider reports Int64.
//
// AI SUMMARY:
// - Snowflake.Data reports every scale-0 NUMBER as Int64; GetValue/GetInt64/GetFieldValue throw
//   OverflowException beyond Int64 ("Use GetString() to handle very large values"), while GetString
//   returns the exact digits (confirmed live 2026-10-02 on Snowflake.Data 5.6).
// - Read(record, ordinal): GetValue, or, when that overflows on a column reported as Int64, the
//   column's text as a BigInteger. Any other failure is left as the provider threw it.
// - Keyed on the provider's behavior, not the database: a column reported as Int64 can only
//   overflow Int64 when the provider understates its type.
// - Used by entity hydration (CompiledMapperFactory, DataReaderMapper) for properties wider than
//   Int64 and by TrackedReader.GetValue. The usual checked casts then convert the BigInteger, so a
//   property that can't hold the value still fails loudly (DataMappingException).
// =============================================================================

using System.Data;
using System.Globalization;
using System.Numerics;

namespace pengdows.crud.@internal;

internal static class WideIntegerFieldReader
{
    public static object Read(IDataRecord record, int ordinal)
    {
        try
        {
            return record.GetValue(ordinal);
        }
        catch (OverflowException) when (TryReadText(record, ordinal, out var value))
        {
            return value;
        }
    }

    /// <summary>
    /// The column's value as a <see cref="BigInteger"/> read from its text, when the provider
    /// reports the column as <see cref="long"/> and the text is an integer.
    /// </summary>
    public static bool TryReadText(IDataRecord record, int ordinal, out object value)
    {
        value = null!;
        try
        {
            if (record.GetFieldType(ordinal) != typeof(long) ||
                !BigInteger.TryParse(record.GetString(ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return false;
            }

            value = parsed;
            return true;
        }
        catch (Exception)
        {
            // The text isn't readable either: the original overflow is the error to report.
            return false;
        }
    }

    /// <summary>
    /// True for a property that can hold an integer beyond <see cref="long"/>'s range, so an
    /// Int64-reported column is read through <see cref="Read"/> rather than GetInt64.
    /// </summary>
    public static bool IsWiderThanInt64(Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type == typeof(ulong) || type == typeof(decimal) || type == typeof(double) ||
               type == typeof(BigInteger) || type == typeof(Int128) || type == typeof(UInt128) ||
               type == typeof(string) || type == typeof(object);
    }
}
