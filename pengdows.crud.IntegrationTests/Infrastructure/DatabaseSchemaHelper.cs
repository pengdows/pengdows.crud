using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

internal static class DatabaseSchemaHelper
{
    public static readonly IReadOnlyList<string> TablesToDrop = new[]
    {
        "test_related",
        "test_table",
        "order_items",
        "user_roles",
        "merge_records",
        "versioned_entities",
        "audited_entity",
        "products",
        "articles",
        "tagged_items",
        "accounts"
    };

    public static async Task DropTablesAsync(IDatabaseContext context)
    {
        foreach (var table in TablesToDrop)
        {
            await TryDropTableAsync(context, table);
        }
    }

    private static async Task TryDropTableAsync(IDatabaseContext context, string tableName)
    {
        var wrapped = context.WrapObjectName(tableName);
        await using var container = context.CreateSqlContainer($"DROP TABLE {wrapped}");
        try
        {
            await container.ExecuteNonQueryAsync();
        }
        catch (DbException ex) when (IsTableMissing(ex.Message))
        {
            // ignore
        }
        catch (Exception ex) when (IsTableMissing(ex.Message))
        {
            // ignore
        }
        catch (Exception ex)
        {
            if (context.Product == SupportedDatabase.Firebird && IsMetadataLock(ex.Message))
            {
                await TryDeleteTableAsync(context, tableName);
                return;
            }

            if (IsTableMissing(ex.Message))
            {
                return;
            }

            throw;
        }
    }

    private static bool IsTableMissing(string message)
    {
        var text = message?.ToLowerInvariant() ?? string.Empty;
        return text.Contains("does not exist")
               || text.Contains("doesn't exist")
               || text.Contains("no such table")
               || text.Contains("unknown table")
               || text.Contains("table not found")
               || text.Contains("invalid object name")
               || text.Contains("ora-00942")
               || text.Contains("table unknown")
               || text.Contains("table with name")
               || text.Contains("catalog error");
    }

    private static bool IsMetadataLock(string message)
    {
        var text = message?.ToLowerInvariant() ?? string.Empty;
        return text.Contains("lock conflict")
               || text.Contains("object table")
               || text.Contains("metadata update")
               || text.Contains("table is in use");
    }

    private static async Task TryDeleteTableAsync(IDatabaseContext context, string tableName)
    {
        var wrapped = context.WrapObjectName(tableName);
        await using var container = context.CreateSqlContainer($"DELETE FROM {wrapped}");
        try
        {
            await container.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (IsTableMissing(ex.Message))
        {
            // ignore
        }
    }
}