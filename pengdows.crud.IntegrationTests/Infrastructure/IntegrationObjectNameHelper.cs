using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.Infrastructure;

internal static class IntegrationObjectNameHelper
{
    public static string Table(IDatabaseContext context, string tableName)
    {
        var parts = GetNamespaceParts(context);
        parts.Add(tableName);
        return string.Join(
            context.CompositeIdentifierSeparator,
            parts.Select(context.WrapObjectName));
    }

    // Consolidated from previously independent, copy-pasted private helpers in
    // Core/AuditFieldTests.cs, Core/CompositeKeyTests.cs, Core/MergeConflictTests.cs, and
    // Core/VersionedUpsertConflictTests.cs — see CLAUDE.md's "Adding a New Database" checklist
    // item 19. A new database's column-type quirk only needs fixing here now, not independently
    // rediscovered in every test file that builds its own table.

    public static string BigIntType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Sqlite => "INTEGER",
        SupportedDatabase.Oracle => "NUMBER(19)",
        _ => "BIGINT"
    };

    public static string IntType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Sqlite => "INTEGER",
        SupportedDatabase.Firebird => "INTEGER",
        _ => "INT"
    };

    public static string StringType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Sqlite => "TEXT",
        SupportedDatabase.SqlServer => "NVARCHAR(255)",
        SupportedDatabase.Oracle => "VARCHAR2(255)",
        SupportedDatabase.Firebird => "VARCHAR(255)",
        _ => "VARCHAR(255)"
    };

    public static string DecimalType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Sqlite => "NUMERIC(18,2)",
        // Spanner's PostgreSQL interface rejects a precision/scale modifier on NUMERIC/DECIMAL
        // outright — verified live: "P0001: Type modifier is not supported for type <numeric>."
        // Spanner's NUMERIC is a fixed-precision type; declare it bare, with no modifier.
        SupportedDatabase.Spanner => "NUMERIC",
        _ => "DECIMAL(18,2)"
    };

    public static string DateTimeType(SupportedDatabase provider) => provider switch
    {
        // SqliteDialect.CreateDbParameter always stores DateTime values as ISO-8601 text
        // (DbType.String, "o" format), so TEXT is the technically correct declared affinity —
        // not DATETIME, which one of the four source helpers this was consolidated from used
        // inconsistently (harmless only because SQLite's NUMERIC affinity for "DATETIME" happens
        // to leave a non-numeric-looking ISO-8601 string stored as text anyway).
        SupportedDatabase.Sqlite => "TEXT",
        SupportedDatabase.SqlServer => "DATETIME2",
        SupportedDatabase.MySql => "DATETIME",
        SupportedDatabase.MariaDb => "DATETIME",
        // Spanner's PostgreSQL interface has no plain TIMESTAMP type at all (verified live:
        // "P0001: Type <timestamp> is not supported.") — only TIMESTAMPTZ, since Spanner always
        // stores instants in UTC internally.
        SupportedDatabase.Spanner => "TIMESTAMPTZ",
        _ => "TIMESTAMP"
    };

    // Spanner's PostgreSQL interface rejects an inline table-level UNIQUE constraint outright —
    // verified live: "P0001: <UNIQUE> constraint is not supported, create a unique index
    // instead." Every other provider gets the normal inline clause; for Spanner, use this (empty)
    // plus SpannerUniqueIndexSql's separate CREATE UNIQUE INDEX statement as a follow-up.
    public static string InlineUniqueConstraintClause(SupportedDatabase provider, params string[] wrappedColumns) =>
        provider == SupportedDatabase.Spanner
            ? string.Empty
            : $",\n    UNIQUE ({string.Join(", ", wrappedColumns)})";

    // Pairs with InlineUniqueConstraintClause: null for every provider except Spanner, which
    // needs this run as a separate statement after CREATE TABLE (see DatabaseSchemaHelper's
    // Spanner-specific "with indices" drop fallback for the matching cleanup-side handling).
    public static string? SpannerUniqueIndexSql(SupportedDatabase provider, IDatabaseContext context,
        string qualifiedTable, string indexName, params string[] wrappedColumns)
    {
        if (provider != SupportedDatabase.Spanner)
        {
            return null;
        }

        return $"CREATE UNIQUE INDEX {context.WrapObjectName(indexName)} ON {qualifiedTable} ({string.Join(", ", wrappedColumns)})";
    }

    private static List<string> GetNamespaceParts(IDatabaseContext context)
    {
        if (!context.Dialect.SupportsNamespaces)
        {
            return [];
        }

        var builder = TryParse(context.ConnectionString);
        return context.Product switch
        {
            SupportedDatabase.Snowflake => GetSnowflakeParts(builder),
            SupportedDatabase.PostgreSql or SupportedDatabase.Spanner or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb
                => [GetPostgreSqlSchema(builder) ?? "public"],
            SupportedDatabase.SqlServer => [GetValue(builder, "Current Schema", "Schema") ?? "dbo"],
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb
                => GetValue(builder, "Database", "Initial Catalog") is { Length: > 0 } db
                    ? [db]
                    : [],
            SupportedDatabase.DuckDB => [GetValue(builder, "schema") ?? "main"],
            // Oracle tables are created in the connected user's schema by default.
            // DatabaseContext.ConnectionString is redacted (User Id → "REDACTED"), so we must
            // not derive a schema prefix from it — doing so produces ORA-01918.
            SupportedDatabase.Oracle => [],
            _ => []
        };
    }

    private static List<string> GetSnowflakeParts(DbConnectionStringBuilder? builder)
    {
        var parts = new List<string>();
        if (GetValue(builder, "db", "database") is { Length: > 0 } database)
        {
            parts.Add(database);
        }

        if (GetValue(builder, "schema") is { Length: > 0 } schema)
        {
            parts.Add(schema);
        }
        else
        {
            parts.Add("PUBLIC");
        }

        return parts;
    }

    private static string? GetPostgreSqlSchema(DbConnectionStringBuilder? builder)
    {
        var searchPath = GetValue(builder, "Search Path", "SearchPath");
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return null;
        }

        return searchPath
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static part => !string.IsNullOrWhiteSpace(part));
    }

    private static DbConnectionStringBuilder? TryParse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            return new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? GetValue(DbConnectionStringBuilder? builder, params string[] keys)
    {
        if (builder is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
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

        return null;
    }
}
