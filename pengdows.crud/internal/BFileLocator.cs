// =============================================================================
// FILE: BFileLocator.cs
// PURPOSE: Reads an Oracle BFILE column as its locator (the directory object and file name), not its content.
//
// AI SUMMARY:
// - On a BFILE column the driver's GetValue returns the file's content bytes (confirmed live with ODP.NET), and its
//   GetProviderSpecificValue returns the locator. A BFILE declared on a column is read through the provider-specific value.
// - Read() unwraps the tracked reader to the provider's reader, falling back to GetValue for any reader without one.
// - Thread-safe: stateless.
// =============================================================================

using System.Data;
using System.Data.Common;

namespace pengdows.crud.@internal;

internal static class BFileLocator
{
    /// <summary>The column's locator as the driver's provider-specific value, or its value when there is none.</summary>
    public static object? Read(IDataRecord record, int ordinal)
    {
        var reader = record as DbDataReader ?? (record as IInternalTrackedReader)?.InnerReader;
        return reader is null ? record.GetValue(ordinal) : reader.GetProviderSpecificValue(ordinal);
    }

    /// <summary>A locator as the text a BFILE column is written from: "DIRECTORY/file".</summary>
    public static string ToText(object locator)
    {
        var type = locator.GetType();
        var directory = type.GetProperty("DirectoryName")?.GetValue(locator) as string;
        var file = type.GetProperty("FileName")?.GetValue(locator) as string;
        return directory + "/" + file;
    }

    /// <summary>The method the mapper calls for a column declared BFILE.</summary>
    public static readonly System.Reflection.MethodInfo ReadMethod = typeof(BFileLocator).GetMethod(nameof(Read))!;

    /// <summary>The method that turns a locator into its text.</summary>
    public static readonly System.Reflection.MethodInfo ToTextMethod = typeof(BFileLocator).GetMethod(nameof(ToText))!;
}
