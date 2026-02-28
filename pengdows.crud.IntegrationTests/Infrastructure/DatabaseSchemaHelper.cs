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
        "Default Order"
    };

    public static async Task DropTablesAsync(IDatabaseContext context)
    {
        if (TryGetResetCommands(context.Product, context.ConnectionString) is { Count: > 0 } resetCommands)
        {
            await ExecuteCommandsAsync(context, resetCommands);
            return;
        }

        foreach (var table in TablesToDrop)
        {
            await TryDropTableAsync(context, table);
        }
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
