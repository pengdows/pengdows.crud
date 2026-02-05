using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace CrudBenchmarks;

internal static class SqlServerSessionSettings
{
    private static readonly string[] Statements =
    {
        "SET ARITHABORT ON",
        "SET ANSI_WARNINGS ON",
        "SET ANSI_NULLS ON",
        "SET QUOTED_IDENTIFIER ON",
        "SET CONCAT_NULL_YIELDS_NULL ON",
        "SET NUMERIC_ROUNDABORT OFF"
    };

    // DBCC USEROPTIONS returns "SET" for enabled options and omits options
    // that are at their default value.  numeric_roundabort OFF is the default,
    // so it never appears in the result set and cannot be asserted here.
    public static IReadOnlyDictionary<string, string> RequiredOptions { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["arithabort"] = "SET",
        ["ansi_warnings"] = "SET",
        ["ansi_nulls"] = "SET",
        ["quoted_identifier"] = "SET",
        ["concat_null_yields_null"] = "SET"
    };

    public static async Task ApplyAsync(DbConnection connection)
    {
        foreach (var statement in Statements)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
