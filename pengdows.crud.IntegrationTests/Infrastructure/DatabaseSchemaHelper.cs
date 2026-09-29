using System.Data.Common;
using System.Diagnostics;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.Infrastructure;

internal static class DatabaseSchemaHelper
{
    public static readonly IReadOnlyList<string> TablesToDrop = new[]
    {
        "test_related",
        "test_table",
        "order_items",
        "user_roles",
        "user_info_temp",
        "returning_test",
        "merge_records",
        "versioned_entities",
        "audited_entity",
        "products",
        "articles",
        "tagged_items",
        "accounts",
        "round_trip_entity",
        "type_hydration",
        "calendar_days",
        "calendar_times",
        "Default Order"
    };

    public static async Task DropTablesAsync(IDatabaseContext context)
    {
        if (TryGetResetCommands(context.Product, context.ConnectionString) is { Count: > 0 } resetCommands)
        {
            await ExecuteCommandsAsync(context, resetCommands);
            return;
        }

        if (context.Product == SupportedDatabase.Spanner)
        {
            await DropSpannerTablesAsync(context);
            return;
        }

        foreach (var table in TablesToDrop)
        {
            await TryDropTableAsync(context, table);
        }
    }

    /// <summary>
    /// Every Spanner DDL statement is a schema change (seconds each, even for a missing table), and
    /// Spanner refuses to drop a table that still has a secondary index. Drops only the tables that
    /// exist, their indexes first, as one DDL batch (one schema change).
    /// </summary>
    private static async Task DropSpannerTablesAsync(IDatabaseContext context)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        await using (var tables = context.CreateSqlContainer(
                         "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"))
        await using (var reader = await tables.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(0));
            }
        }

        var toDrop = TablesToDrop.Where(existing.Contains).ToList();
        if (toDrop.Count == 0)
        {
            return;
        }

        var indexes = new List<string>();
        // Only user-created indexes: the ones Spanner manages for foreign keys (spanner_is_managed)
        // can't be dropped ("It is in use by foreign keys") and go away with the table.
        await using (var indexQuery = context.CreateSqlContainer(
                         "SELECT table_name, index_name, CAST(spanner_is_managed AS VARCHAR) " +
                         "FROM information_schema.indexes WHERE table_schema = 'public' AND index_type = 'INDEX'"))
        await using (var reader = await indexQuery.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var managed = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var isManaged = managed.Equals("YES", StringComparison.OrdinalIgnoreCase)
                                || managed.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (!isManaged && toDrop.Contains(reader.GetString(0)))
                {
                    indexes.Add(reader.GetString(1));
                }
            }
        }

        var statements = indexes.Select(index => $"DROP INDEX {context.WrapObjectName(index)}")
            .Concat(toDrop.Select(table => $"DROP TABLE {IntegrationObjectNameHelper.Table(context, table)}"));
        await using var drop = context.CreateSqlContainer(string.Join(";\n", statements));
        await drop.ExecuteNonQueryAsync();
    }

    internal static IReadOnlyList<string>? TryGetResetCommands(SupportedDatabase provider, string connectionString)
    {
        if (provider != SupportedDatabase.Snowflake)
        {
            return null;
        }

        var schemaName = TryGetConnectionStringValue(connectionString, "schema");
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return null;
        }

        var databaseName = TryGetConnectionStringValue(connectionString, "db");
        var wrappedSchema = QuoteIdentifier(schemaName);
        var commands = new List<string>
        {
            $"DROP SCHEMA IF EXISTS {wrappedSchema} CASCADE",
            $"CREATE SCHEMA IF NOT EXISTS {wrappedSchema}"
        };

        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            commands.Add($"USE DATABASE {QuoteIdentifier(databaseName)}");
        }

        commands.Add($"USE SCHEMA {wrappedSchema}");
        return commands;
    }

    private static async Task TryDropTableAsync(IDatabaseContext context, string tableName)
    {
        var wrapped = IntegrationObjectNameHelper.Table(context, tableName);
        await using var container = context.CreateSqlContainer($"DROP TABLE {wrapped}");
        var traceEnabled = IntegrationTraceLog.IsEnabled(context.Product);
        var sw = traceEnabled ? Stopwatch.StartNew() : null;

        if (traceEnabled)
        {
            IntegrationTraceLog.Write(context.Product, $"DROP start table={tableName}");
        }

        try
        {
            await container.ExecuteNonQueryAsync();
            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product,
                    $"DROP done table={tableName} elapsedMs={sw!.ElapsedMilliseconds}");
            }
        }
        catch (DbException ex) when (IsTableMissing(ex.Message))
        {
            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product,
                    $"DROP skip-missing table={tableName} elapsedMs={sw!.ElapsedMilliseconds}");
            }
            // ignore
        }
        catch (Exception ex) when (IsTableMissing(ex.Message))
        {
            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product,
                    $"DROP skip-missing table={tableName} elapsedMs={sw!.ElapsedMilliseconds}");
            }
            // ignore
        }
        catch (Exception ex)
        {
            if (context.Product == SupportedDatabase.Firebird && IsMetadataLock(ex.Message))
            {
                if (traceEnabled)
                {
                    IntegrationTraceLog.Write(context.Product,
                        $"DROP fallback-delete table={tableName} elapsedMs={sw!.ElapsedMilliseconds} error={ex.Message}");
                }
                await TryDeleteTableAsync(context, tableName);
                return;
            }

            if (IsTableMissing(ex.Message))
            {
                if (traceEnabled)
                {
                    IntegrationTraceLog.Write(context.Product,
                        $"DROP skip-missing table={tableName} elapsedMs={sw!.ElapsedMilliseconds}");
                }
                return;
            }

            if (await TryDropSpannerBlockingIndicesAsync(context, ex))
            {
                await TryDropTableAsync(context, tableName);
                return;
            }

            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product,
                    $"DROP fail table={tableName} elapsedMs={sw!.ElapsedMilliseconds} error={ex.Message}");
            }
            throw;
        }
    }

    private static async Task ExecuteCommandsAsync(IDatabaseContext context, IReadOnlyList<string> commands)
    {
        var traceEnabled = IntegrationTraceLog.IsEnabled(context.Product);

        for (var i = 0; i < commands.Count; i++)
        {
            await using var container = context.CreateSqlContainer(commands[i]);
            var sw = traceEnabled ? Stopwatch.StartNew() : null;

            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product, $"RESET start commandIndex={i} sql={commands[i]}");
            }

            await container.ExecuteNonQueryAsync();

            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product,
                    $"RESET done commandIndex={i} elapsedMs={sw!.ElapsedMilliseconds}");
            }
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
               || text.Contains("catalog error")
               // Db2: SQL0204N "<schema>.<name> is an undefined name."
               || text.Contains("sql0204n")
               || text.Contains("is an undefined name")
               // Informix: "The specified table (<name>) is not in the database."
               || text.Contains("is not in the database")
               // SAP HANA (error 259): "invalid table name: <name>"
               || text.Contains("invalid table name");
    }

    /// <summary>
    /// Spanner's PostgreSQL interface refuses to drop a table that still has a secondary index
    /// ("Cannot drop table merge_records with indices: ux_merge_records_record_key."), where
    /// PostgreSQL drops dependent indexes itself. Drops the indexes the message names and returns
    /// true so the caller retries the DROP TABLE; returns false for any other error.
    /// </summary>
    internal static async Task<bool> TryDropSpannerBlockingIndicesAsync(IDatabaseContext context, Exception ex)
    {
        if (context.Product != SupportedDatabase.Spanner)
        {
            return false;
        }

        const string marker = "with indices:";
        var message = ex.Message;
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var tail = message[(markerIndex + marker.Length)..];
        var end = tail.IndexOfAny(['.', '\n', '\r']);
        if (end >= 0)
        {
            tail = tail[..end];
        }

        var indices = tail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (indices.Length == 0)
        {
            return false;
        }

        foreach (var index in indices)
        {
            await using var dropIndex = context.CreateSqlContainer($"DROP INDEX {context.WrapObjectName(index)}");
            await dropIndex.ExecuteNonQueryAsync();
        }

        return true;
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
        var wrapped = IntegrationObjectNameHelper.Table(context, tableName);
        await using var container = context.CreateSqlContainer($"DELETE FROM {wrapped}");
        var traceEnabled = IntegrationTraceLog.IsEnabled(context.Product);
        var sw = traceEnabled ? Stopwatch.StartNew() : null;

        if (traceEnabled)
        {
            IntegrationTraceLog.Write(context.Product, $"DELETE fallback start table={tableName}");
        }

        try
        {
            await container.ExecuteNonQueryAsync();
            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product,
                    $"DELETE fallback done table={tableName} elapsedMs={sw!.ElapsedMilliseconds}");
            }
        }
        catch (Exception ex) when (IsTableMissing(ex.Message))
        {
            if (traceEnabled)
            {
                IntegrationTraceLog.Write(context.Product,
                    $"DELETE fallback skip-missing table={tableName} elapsedMs={sw!.ElapsedMilliseconds}");
            }
            // ignore
        }
    }

    private static string? TryGetConnectionStringValue(string connectionString, string key)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            foreach (var builderKey in builder.Keys)
            {
                var candidate = builderKey?.ToString();
                if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return builder[candidate!]?.ToString();
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        return null;
    }

    private static string QuoteIdentifier(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
