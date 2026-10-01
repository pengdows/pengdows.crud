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

    /// <summary>
    /// Drops <paramref name="tableName"/> if it exists, recovering from two known
    /// provider-specific failure shapes rather than propagating them: Spanner's refusal to drop a
    /// table that still has a secondary index (parses the blocking index name(s) out of the error
    /// and drops them first, then retries), and Firebird's DDL-vs-connection-pooling metadata
    /// lock. Used both by the shared per-run fixture cleanup (<see cref="DropTablesAsync"/>) and
    /// by individual test classes' own per-class table recreation
    /// (<c>DatabaseTestBase.DropTableIfExistsAsync</c>) — a test class that only calls the latter
    /// without going through this method misses both fallbacks, which is exactly the gap that let
    /// Spanner/Firebird failures slip through CompositeKeyTests/MergeConflictTests despite this
    /// method already handling them correctly for the fixture-wide cleanup path.
    /// </summary>
    /// <param name="requireActualDrop">
    /// When <see langword="false"/> (the default, used by <see cref="DropTablesAsync"/>'s own
    /// fixture-wide reset): Firebird's metadata lock falls back to <c>DELETE FROM</c>, which
    /// needs no DDL lock at all — sufficient there because that caller only needs the table
    /// EMPTY, not gone, before the next test's own setup runs. When <see langword="true"/> (used
    /// by <c>DatabaseTestBase.DropTableIfExistsAsync</c>, whose callers immediately issue a bare
    /// <c>CREATE TABLE</c> expecting the name to be free): the DELETE fallback is skipped and the
    /// original exception is rethrown instead, so the caller's own outer retry loop gets a
    /// genuine re-attempt at the real DROP — settling for "emptied, not dropped" here would leave
    /// the table behind and turn that immediately-following CREATE TABLE into a hard
    /// "already exists" failure, which is exactly the regression this parameter exists to avoid.
    /// </param>
    internal static async Task TryDropTableAsync(IDatabaseContext context, string tableName,
        bool requireActualDrop = false)
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
            if (context.Product == SupportedDatabase.Firebird && IsMetadataLock(ex.Message) && !requireActualDrop)
            {
                if (traceEnabled)
                {
                    IntegrationTraceLog.Write(context.Product,
                        $"DROP fallback-delete table={tableName} elapsedMs={sw!.ElapsedMilliseconds} error={ex.Message}");
                }
                await TryDeleteTableAsync(context, tableName);
                return;
            }

            // Spanner's PostgreSQL interface refuses to drop a table that still has a
            // secondary (non-PK) index on it — verified live: "Cannot drop table
            // merge_records with indices: ux_merge_records_record_key." Real PostgreSQL drops
            // dependent indices automatically; Spanner requires them dropped first. The message
            // lists the exact index name(s), so drop each one and retry rather than hardcoding
            // any particular table/index pair here.
            if (context.Product == SupportedDatabase.Spanner && TryGetSpannerBlockingIndices(ex.Message) is { Count: > 0 } indices)
            {
                foreach (var index in indices)
                {
                    await using var dropIndex = context.CreateSqlContainer($"DROP INDEX {context.WrapObjectName(index)}");
                    await dropIndex.ExecuteNonQueryAsync();
                }

                if (traceEnabled)
                {
                    IntegrationTraceLog.Write(context.Product,
                        $"DROP fallback-drop-indices table={tableName} indices={string.Join(",", indices)} elapsedMs={sw!.ElapsedMilliseconds}");
                }

                await TryDropTableAsync(context, tableName, requireActualDrop);
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

    // Parses Spanner's "Cannot drop table <name> with indices: <idx1>, <idx2>" message shape to
    // recover the exact index name(s) that need dropping first. Returns null (not empty) when the
    // message doesn't match at all, so the caller can distinguish "not this error" from "matched,
    // but somehow zero names" (which would be a parsing bug worth investigating rather than
    // silently no-op'ing a retry loop).
    private static List<string>? TryGetSpannerBlockingIndices(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        const string marker = "with indices:";
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var tail = message[(markerIndex + marker.Length)..];
        var end = tail.IndexOfAny(['.', '\n', '\r']);
        if (end >= 0)
        {
            tail = tail[..end];
        }

        return tail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0)
            .ToList();
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
