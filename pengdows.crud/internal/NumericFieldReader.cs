// =============================================================================
// FILE: NumericFieldReader.cs
// PURPOSE: Reads a decimal-typed column into a double/float property when the value is beyond decimal.
//
// AI SUMMARY:
// - ODP.NET reports NUMBER/FLOAT as decimal; GetValue/GetDecimal throw InvalidCastException for a
//   value beyond decimal (1.25e100 in a FLOAT(126) column), while GetDouble reads it (confirmed live,
//   Oracle 23ai, TYPE-002).
// - ReadDouble(): GetDecimal converted to double, or GetDouble when the value is beyond decimal.
//   Typed, so the common case (any provider's decimal into a double property) neither boxes nor
//   goes through the general coercion.
// - Used by entity hydration (CompiledMapperFactory, DataReaderMapper) for double/float properties
//   on columns the provider reports as decimal.
// =============================================================================

using System.Data;
using System.Globalization;

namespace pengdows.crud.@internal;

internal static class NumericFieldReader
{
    public static double ReadDouble(IDataRecord record, int ordinal)
    {
        try
        {
            return (double)record.GetDecimal(ordinal);
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException)
        {
            return record.GetDouble(ordinal);
        }
    }

    /// <summary>True for a double/float property (nullable or not).</summary>
    public static bool IsFloatingPoint(Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type == typeof(double) || type == typeof(float);
    }
}
