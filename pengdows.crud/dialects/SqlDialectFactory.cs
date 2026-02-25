// =============================================================================
// FILE: SqlDialectFactory.cs
// PURPOSE: Factory for creating database-specific ISqlDialect instances.
//
// AI SUMMARY:
// - CreateDialectAsync() - Creates and initializes dialect from live connection.
// - CreateDialectForType() - Creates dialect for known SupportedDatabase type.
// - Auto-detection flow: Delegates to DatabaseDetectionService for robust identification.
// - Supported dialects: SqlServer, PostgreSql, MySql, MariaDb, Oracle,
//   Sqlite, Firebird, DuckDb, CockroachDb, YugabyteDb, TiDb, Snowflake.
// - Each dialect is initialized via DetectDatabaseInfoAsync() after creation.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using pengdows.crud.@internal;

namespace pengdows.crud.dialects;

/// <summary>
/// Factory for creating database-specific dialect instances with automatic detection.
/// </summary>
public static class SqlDialectFactory
{
    public static async Task<ISqlDialect> CreateDialectAsync(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<SqlDialect>();

        // Use centralized detection service
        var inferredType = DatabaseDetectionService.DetectProduct(connection, factory);

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
            SupportedDatabase.CockroachDb => new CockroachDbDialect(factory, logger),
            SupportedDatabase.YugabyteDb => new YugabyteDbDialect(factory, logger),
            SupportedDatabase.TiDb => new TiDbDialect(factory, logger),
            SupportedDatabase.MySql => new MySqlDialect(factory, logger),
            SupportedDatabase.AuroraMySql => new MySqlDialect(factory, logger, SupportedDatabase.AuroraMySql),
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, logger),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, logger),
            SupportedDatabase.Oracle => new OracleDialect(factory, logger),
            SupportedDatabase.Firebird => new FirebirdDialect(factory, logger),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, logger),
            SupportedDatabase.Snowflake => new SnowflakeDialect(factory, logger),
            SupportedDatabase.AuroraPostgreSql => new PostgreSqlDialect(factory, logger, SupportedDatabase.AuroraPostgreSql),
            _ => new Sql92Dialect(factory, logger)
        };
    }

    private static SupportedDatabase InferDatabaseTypeFromProvider(DbProviderFactory factory)
    {
        return DatabaseDetectionService.DetectFromFactory(factory);
    }

    private static SupportedDatabase InferDatabaseTypeFromName(string name)
    {
        return DatabaseDetectionService.DetectFromName(name);
    }

    private static Task<SupportedDatabase> InferDatabaseTypeFromConnectionAsync(
        ITrackedConnection connection,
        ILogger logger)
    {
        return Task.FromResult(DatabaseDetectionService.DetectFromConnection(connection));
    }
}