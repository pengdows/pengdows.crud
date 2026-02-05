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

    public static IReadOnlyDictionary<string, string> RequiredOptions { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["arithabort"] = "on",
        ["ansi_warnings"] = "on",
        ["ansi_nulls"] = "on",
        ["quoted_identifier"] = "on",
        ["concat_null_yields_null"] = "on",
        ["numeric_roundabort"] = "off"
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
