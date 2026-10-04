// =============================================================================
// FILE: SqlDialectFactory.cs
// PURPOSE: Factory for creating database-specific ISqlDialect instances.
//
// AI SUMMARY:
// - CreateDialectAsync() - Creates and initializes dialect from live connection.
// - CreateDialectForType() - Creates dialect for known SupportedDatabase type.
// - Auto-detection flow: Delegates to DatabaseDetectionService for robust identification.
// - Supported dialects: SqlServer, PostgreSql, AuroraPostgreSql, CockroachDb, YugabyteDb,
//   Spanner, MySql, AuroraMySql, SingleStore, MariaDb, TiDb, Oracle, Sqlite, Firebird,
//   InterBase, DuckDb, Snowflake, SybaseASE, Db2, Informix, SapHana, Access, FlatFile;
//   anything else gets the Sql92Dialect fallback. TimescaleDB has no separate value and
//   uses PostgreSqlDialect.
// - CreateDialectAsync() initializes the dialect via DetectDatabaseInfoAsync();
//   CreateDialectForType() returns it uninitialized.
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
    internal static Task<ISqlDialect> CreateDialectAsync(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        return CreateDialectCoreAsync(connection, factory, loggerFactory, true, cancellationToken).AsTask();
    }

    internal static ISqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory)
    {
        return CreateDialect(connection, factory, NullLoggerFactory.Instance);
    }


    internal static ISqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        // Synchronous product and version detection throughout (REV-044).
        return CreateDialectCoreAsync(connection, factory, loggerFactory, false, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// The one dialect-creation path (BP-311): <paramref name="useAsync"/> picks synchronous or
    /// asynchronous product-detection probes, so the constructor path keeps its synchronous probes
    /// and <c>DatabaseContext.CreateAsync</c> never runs a blocking one.
    /// </summary>
    private static async ValueTask<ISqlDialect> CreateDialectCoreAsync(
        ITrackedConnection connection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory,
        bool useAsync,
        CancellationToken cancellationToken)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        cancellationToken.ThrowIfCancellationRequested();
        var logger = loggerFactory.CreateLogger<SqlDialect>();

        var inferredType = useAsync
            ? await DatabaseDetectionService.DetectProductAsync(connection, factory, cancellationToken)
                .ConfigureAwait(false)
            : DatabaseDetectionService.DetectProduct(connection, factory);

        var dialect = CreateDialectForType(inferredType, factory, logger);
        if (dialect is not IInternalSqlDialect internalDialect)
        {
            throw new InvalidOperationException("Dialect must support internal detection operations.");
        }

        // Trust the detection pass we just ran instead of letting DetectDatabaseInfoAsync
        // independently re-derive (and potentially disagree with) the same answer.
        if (dialect is SqlDialect concreteDialect)
        {
            concreteDialect.PreDeterminedDatabaseType = inferredType;
        }

        if (dialect is SqlDialect detecting)
        {
            // useAsync false issues only synchronous commands, so the constructor path no longer
            // blocks on async version detection (REV-044).
            await detecting.DetectDatabaseInfoCoreAsync(connection, useAsync).ConfigureAwait(false);
        }
        else
        {
            await internalDialect.DetectDatabaseInfoAsync(connection).ConfigureAwait(false);
        }
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
            SupportedDatabase.SingleStore => new MySqlDialect(factory, logger, SupportedDatabase.SingleStore),
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, logger),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, logger),
            SupportedDatabase.Oracle => new OracleDialect(factory, logger),
            SupportedDatabase.Firebird => new FirebirdDialect(factory, logger),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, logger),
            SupportedDatabase.Snowflake => new SnowflakeDialect(factory, logger),
            SupportedDatabase.AuroraPostgreSql => new PostgreSqlDialect(factory, logger, SupportedDatabase.AuroraPostgreSql),
            SupportedDatabase.FlatFile => new FlatFileDialect(factory, logger),
            SupportedDatabase.SybaseASE => new SybaseAseDialect(factory, logger),
            SupportedDatabase.Db2 => new Db2Dialect(factory, logger),
            SupportedDatabase.Informix => new InformixDialect(factory, logger),
            SupportedDatabase.SapHana => new HanaDialect(factory, logger),
            SupportedDatabase.InterBase => new InterBaseDialect(factory, logger),
            SupportedDatabase.Spanner => new SpannerDialect(factory, logger),
            SupportedDatabase.Access => new AccessDialect(factory, logger),
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
