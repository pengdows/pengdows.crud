using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.IntegrationTests.Infrastructure;

internal static class IntegrationObjectNameHelper
{
    /// <summary>
    /// False where a unique key on columns other than the primary (shard) key cannot be declared at
    /// all: SingleStore rejects it ("unique keys must contain all columns of the shard key", confirmed
    /// live on the 3.0 branch; its dialect reports SupportsUniqueConstraints = false). Tests that need
    /// the database to reject a duplicate business key skip on this.
    /// </summary>
    public static bool CanDeclareSecondaryUniqueKey(IDatabaseContext context) =>
        context.Product != SupportedDatabase.SingleStore;

    /// <summary>
    /// An inline table-level UNIQUE clause (with its leading comma), or empty where the database
    /// cannot declare it inline. Spanner's PostgreSQL interface rejects inline UNIQUE ("create a
    /// unique index instead"; see <see cref="SpannerUniqueIndexSql"/>), and see
    /// <see cref="CanDeclareSecondaryUniqueKey"/>.
    /// </summary>
    public static string InlineUniqueConstraintClause(IDatabaseContext context, params string[] wrappedColumns) =>
        context.Product == SupportedDatabase.Spanner || !CanDeclareSecondaryUniqueKey(context)
            ? string.Empty
            : $",\n    UNIQUE ({string.Join(", ", wrappedColumns)})";

    /// <summary>
    /// The separate CREATE UNIQUE INDEX Spanner needs in place of an inline UNIQUE clause, or null
    /// for every other database.
    /// </summary>
    public static string? SpannerUniqueIndexSql(IDatabaseContext context, string qualifiedTable,
        string indexName, params string[] wrappedColumns)
    {
        if (context.Product != SupportedDatabase.Spanner)
        {
            return null;
        }

        return $"CREATE UNIQUE INDEX {context.WrapObjectName(indexName)} ON {qualifiedTable} ({string.Join(", ", wrappedColumns)})";
    }

    public static string Table(IDatabaseContext context, string tableName)
    {
        var parts = GetNamespaceParts(context);
        parts.Add(tableName);
        return string.Join(
            context.CompositeIdentifierSeparator,
            parts.Select(context.WrapObjectName));
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
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb
                => [GetPostgreSqlSchema(builder) ?? "public"],
            SupportedDatabase.SqlServer => [GetValue(builder, "Current Schema", "Schema") ?? "dbo"],
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb
                or SupportedDatabase.SingleStore
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
