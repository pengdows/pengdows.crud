// =============================================================================
// FILE: SqlDialectFactory.cs
// PURPOSE: Factory for creating database-specific ISqlDialect instances.
//
// AI SUMMARY:
// - CreateDialectAsync() - Creates and initializes dialect from live connection.
// - CreateDialectForType() - Creates dialect for known SupportedDatabase type.
// - Auto-detection flow: Delegates to DatabaseDetectionService for robust identification.
// - Supported dialects: SqlServer, PostgreSql, MySql, AuroraMySql, MariaDb, Oracle,
//   Sqlite, Firebird, DuckDb, CockroachDb, YugabyteDb, TiDb, Snowflake, AuroraPostgreSql.
//   TimescaleDB is detected at runtime and routed to PostgreSqlDialect.
// - Each dialect is initialized via DetectDatabaseInfoAsync() after creation.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using pengdows.crud.@internal;

namespace pengdows.crud.dialects;

/// <summary>
/// Factory for creating database-specific dialect instances with automatic detection.
/// </summary>
internal static class SqlDialectFactory
{
    internal static async Task<ISqlDialect> CreateDialectAsync(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<SqlDialect>();

        // Use centralized detection service
        var inferredType = await DatabaseDetectionService.DetectProductAsync(connection, factory).ConfigureAwait(false);

        var dialect = CreateDialectForType(inferredType, factory, logger);
        if (dialect is not IInternalSqlDialect internalDialect)
        {
            throw new InvalidOperationException("Dialect must support internal detection operations.");
        }

        await internalDialect.DetectDatabaseInfoAsync(connection).ConfigureAwait(false);
        return dialect;
    }

    internal static ISqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory)
    {
        return CreateDialect(connection, factory, NullLoggerFactory.Instance);
    }

    // Deliberately independent of CreateDialectAsync: this is the entry point used by the fully
    // synchronous DatabaseContext constructor (via each IConnectionStrategy.HandleDialectDetection).
    // Delegating to CreateDialectAsync().GetAwaiter().GetResult() would route product identification
    // through DetectProductAsync/ExecuteScalarAsync for a call site that never awaits anything —
    // pure sync-over-async with no benefit. DetectDatabaseInfoAsync below is unavoidably
    // sync-over-async regardless (GetDatabaseVersionAsync/GetProductNameAsync have no sync
    // implementation — see SqlDialect.GetDatabaseVersion, itself just
    // GetDatabaseVersionAsync(...).GetAwaiter().GetResult()), so this only saves the one step that
    // actually has a genuine synchronous path.
    internal static ISqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<SqlDialect>();

        var inferredType = DatabaseDetectionService.DetectProduct(connection, factory);

        var dialect = CreateDialectForType(inferredType, factory, logger);
        if (dialect is not IInternalSqlDialect internalDialect)
        {
            throw new InvalidOperationException("Dialect must support internal detection operations.");
        }

        internalDialect.DetectDatabaseInfoAsync(connection).GetAwaiter().GetResult();
        return dialect;
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
