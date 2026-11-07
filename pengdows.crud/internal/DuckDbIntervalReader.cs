// =============================================================================
// FILE: DuckDbIntervalReader.cs
// PURPOSE: Reads a DuckDB interval as its months, days and microseconds.
//
// AI SUMMARY:
// - DuckDB.NET's reader returns an interval through GetFieldValue<DuckDBInterval>, a struct of Months, Days and Micros. Its plain
//   GetValue is a TimeSpan, which cannot hold months ("a month has no fixed length").
// - ReadValue(): for a DuckDB reader's interval column, the struct as a PostgreSqlInterval; for any other reader, the
//   ordinary GetValue, unchanged.
// - The struct is read by its members (reflection, cached per reader type), so this file does not reference DuckDB.NET.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.@internal;

internal static class DuckDbIntervalReader
{
    // DuckDB.NET's interval struct (in its bindings assembly), and the reader's GetFieldValue<T> for it.
    private static readonly Lazy<MethodInfo?> FieldValueOfInterval = new(() =>
        Type.GetType("DuckDB.NET.Native.DuckDBInterval, DuckDB.NET.Bindings") is { } intervalType
            ? typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFieldValue), new[] { typeof(int) })!
                .MakeGenericMethod(intervalType)
            : null);

    /// <summary>Whether a reader type is DuckDB.NET's.</summary>
    internal static bool IsDuckDbReaderTypeName(string? readerTypeName) =>
        ProviderTypeNames.IsDuckDbReaderTypeName(readerTypeName);

    /// <summary>The column's value: a DuckDB interval as a <see cref="PostgreSqlInterval"/>; anything else as GetValue.</summary>
    public static object ReadValue(IDataRecord record, int ordinal)
    {
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        if (reader != null && IsDuckDbReaderTypeName(reader.GetType().FullName) &&
            string.Equals(reader.GetDataTypeName(ordinal), "INTERVAL", StringComparison.OrdinalIgnoreCase) &&
            FieldValueOfInterval.Value is { } read)
        {
            return ToPostgreSqlInterval(read.Invoke(reader, new object[] { ordinal })!);
        }

        return record.GetValue(ordinal);
    }

    /// <summary>A DuckDB interval struct (Months, Days, Micros) as the interval value.</summary>
    internal static PostgreSqlInterval ToPostgreSqlInterval(object interval)
    {
        var type = interval.GetType();
        // The members are integers of the provider's width (its microseconds are UInt64): each is read as its value.
        var months = checked((int)Convert.ToInt64(type.GetProperty("Months")!.GetValue(interval), CultureInfo.InvariantCulture));
        var days = checked((int)Convert.ToInt64(type.GetProperty("Days")!.GetValue(interval), CultureInfo.InvariantCulture));
        var micros = checked((long)Convert.ToUInt64(type.GetProperty("Micros")!.GetValue(interval), CultureInfo.InvariantCulture));
        return new PostgreSqlInterval(months, days, micros);
    }
}
