// =============================================================================
// FILE: SqlDialectFactory.cs
// PURPOSE: Factory for creating database-specific ISqlDialect instances.
//
// AI SUMMARY:
// - CreateDialectAsync() - Creates and initializes dialect from live connection.
// - CreateDialectForType() - Creates dialect for known SupportedDatabase type.
// - Auto-detection flow:
//   1. Try to infer type from connection metadata (ServerVersion, GetSchema)
//   2. Fall back to provider type name matching
//   3. Use Sql92Dialect if all detection fails
// - Supported dialects: SqlServer, PostgreSql, MySql, MariaDb, Oracle,
//   Sqlite, Firebird, DuckDb, CockroachDb (uses PostgreSql dialect)
// - Each dialect is initialized via DetectDatabaseInfoAsync() after creation.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Factory for creating database-specific dialect instances with automatic detection.
/// </summary>
/// <remarks>
/// <para>
/// This factory creates the appropriate <see cref="ISqlDialect"/> implementation
/// based on the database type, either detected automatically from the connection
/// or specified explicitly.
/// </para>
/// <para>
/// <strong>Detection Order:</strong>
/// </para>
/// <list type="number">
/// <item><description>Query connection metadata (ServerVersion, database functions)</description></item>
/// <item><description>Match provider factory type name</description></item>
/// <item><description>Fall back to SQL-92 dialect</description></item>
/// </list>
/// </remarks>
public static class SqlDialectFactory
{
    public static async Task<ISqlDialect> CreateDialectAsync(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<SqlDialect>();

        var inferredType = await InferDatabaseTypeFromConnectionAsync(connection, logger).ConfigureAwait(false);
        if (inferredType == SupportedDatabase.Unknown)
        {
            inferredType = InferDatabaseTypeFromProvider(factory);
        }

        var dialect = CreateDialectForType(inferredType, factory, logger);
        await dialect.DetectDatabaseInfoAsync(connection).ConfigureAwait(false);
        return dialect;
    }

    public static ISqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory)
    {
        return CreateDialectAsync(connection, factory, NullLoggerFactory.Instance).GetAwaiter().GetResult();
    }


    public static ISqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        return CreateDialectAsync(connection, factory, loggerFactory).GetAwaiter().GetResult();
    }

    public static ISqlDialect CreateDialectForType(
        SupportedDatabase databaseType,
        DbProviderFactory factory,
        ILogger logger)
    {
        return databaseType switch
        {
            SupportedDatabase.SqlServer => new SqlServerDialect(factory, logger),
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, logger),
            SupportedDatabase.CockroachDb => new PostgreSqlDialect(factory, logger),
            SupportedDatabase.MySql => new MySqlDialect(factory, logger),
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, logger),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, logger),
            SupportedDatabase.Oracle => new OracleDialect(factory, logger),
            SupportedDatabase.Firebird => new FirebirdDialect(factory, logger),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, logger),
            _ => new Sql92Dialect(factory, logger)
        };
    }

    private static SupportedDatabase InferDatabaseTypeFromProvider(DbProviderFactory factory)
    {
        var typeName = factory.GetType().Name.ToLowerInvariant();

        return typeName switch
        {
            var name when name.Contains("sqlserver") || name.Contains("system.data.sqlclient") => SupportedDatabase
                .SqlServer,
            var name when name.Contains("npgsql") => SupportedDatabase.PostgreSql,
            var name when name.Contains("mariadb") => SupportedDatabase.MariaDb,
            var name when name.Contains("mysql") => SupportedDatabase.MySql,
            var name when name.Contains("sqlite") => SupportedDatabase.Sqlite,
            var name when name.Contains("oracle") => SupportedDatabase.Oracle,
            var name when name.Contains("firebird") => SupportedDatabase.Firebird,
            var name when name.Contains("duckdb") => SupportedDatabase.DuckDB,
            _ => SupportedDatabase.Unknown
        };
    }

    private static Task<SupportedDatabase> InferDatabaseTypeFromConnectionAsync(
        ITrackedConnection connection,
        ILogger logger)
    {
        try
        {
            var schema = connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
            if (schema.Rows.Count > 0)
            {
                var name = schema.Rows[0].Field<string>("DataSourceProductName") ?? string.Empty;
                var version = schema.Rows[0].Field<string>("DataSourceProductVersion") ?? string.Empty;
                var inferred = InferDatabaseTypeFromName(name);
                if (inferred == SupportedDatabase.MySql && ContainsMariaDb(version, connection.ServerVersion))
                {
                    return Task.FromResult(SupportedDatabase.MariaDb);
                }

                if (inferred != SupportedDatabase.Unknown)
                {
                    return Task.FromResult(inferred);
                }
            }

            if (ContainsMariaDb(connection.ServerVersion))
            {
                return Task.FromResult(SupportedDatabase.MariaDb);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to infer database type from connection");
        }

        return Task.FromResult(SupportedDatabase.Unknown);
    }

    private static bool ContainsMariaDb(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                value.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static SupportedDatabase InferDatabaseTypeFromName(string name)
    {
        var lower = name?.ToLowerInvariant() ?? string.Empty;

        return lower switch
        {
            var n when n.Contains("sql server") => SupportedDatabase.SqlServer,
            var n when n.Contains("mariadb") => SupportedDatabase.MariaDb,
            var n when n.Contains("mysql") => SupportedDatabase.MySql,
            var n when n.Contains("cockroach") => SupportedDatabase.CockroachDb,
            var n when n.Contains("npgsql") || n.Contains("postgres") => SupportedDatabase.PostgreSql,
            var n when n.Contains("oracle") => SupportedDatabase.Oracle,
            var n when n.Contains("sqlite") => SupportedDatabase.Sqlite,
            var n when n.Contains("firebird") => SupportedDatabase.Firebird,
            var n when n.Contains("duckdb") || n.Contains("duck db") => SupportedDatabase.DuckDB,
            _ => SupportedDatabase.Unknown
        };
    }
}