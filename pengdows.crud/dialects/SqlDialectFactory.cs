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
// - CreateTraits() maps each SupportedDatabase value to the instance-free DatabaseTraits its
//   dialect class declares (REV-039); both read the one Registrations table.
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

    // One row per database: its dialect and its instance-free traits, so the two can't fall out of
    // step (DRY-019: they were two parallel switches). Anything else gets the SQL-92 fallback.
    private static readonly Dictionary<SupportedDatabase,
        (Func<DbProviderFactory, ILogger, ISqlDialect> Create, Func<DatabaseTraits> Traits)> Registrations = new()
    {
        [SupportedDatabase.SqlServer] = (static (factory, logger) => new SqlServerDialect(factory, logger), static () => SqlServerDialect.CreateSqlServerTraits()),
        [SupportedDatabase.PostgreSql] = (static (factory, logger) => new PostgreSqlDialect(factory, logger), static () => PostgreSqlDialect.CreatePostgreSqlTraits()),
        [SupportedDatabase.CockroachDb] = (static (factory, logger) => new CockroachDbDialect(factory, logger), static () => CockroachDbDialect.CreateCockroachDbTraits()),
        [SupportedDatabase.YugabyteDb] = (static (factory, logger) => new YugabyteDbDialect(factory, logger), static () => YugabyteDbDialect.CreateYugabyteDbTraits()),
        [SupportedDatabase.TiDb] = (static (factory, logger) => new TiDbDialect(factory, logger), static () => TiDbDialect.CreateTiDbTraits()),
        [SupportedDatabase.MySql] = (static (factory, logger) => new MySqlDialect(factory, logger), static () => MySqlDialect.CreateMySqlTraits()),
        [SupportedDatabase.AuroraMySql] = (static (factory, logger) => new MySqlDialect(factory, logger, SupportedDatabase.AuroraMySql), static () => MySqlDialect.CreateAuroraMySqlTraits()),
        [SupportedDatabase.SingleStore] = (static (factory, logger) => new MySqlDialect(factory, logger, SupportedDatabase.SingleStore), static () => MySqlDialect.CreateSingleStoreTraits()),
        [SupportedDatabase.MariaDb] = (static (factory, logger) => new MariaDbDialect(factory, logger), static () => MariaDbDialect.CreateMariaDbTraits()),
        [SupportedDatabase.Sqlite] = (static (factory, logger) => new SqliteDialect(factory, logger), static () => SqliteDialect.CreateSqliteTraits()),
        [SupportedDatabase.Oracle] = (static (factory, logger) => new OracleDialect(factory, logger), static () => OracleDialect.CreateOracleTraits()),
        [SupportedDatabase.Firebird] = (static (factory, logger) => new FirebirdDialect(factory, logger), static () => FirebirdDialect.CreateFirebirdTraits()),
        [SupportedDatabase.DuckDB] = (static (factory, logger) => new DuckDbDialect(factory, logger), static () => DuckDbDialect.CreateDuckDbTraits()),
        [SupportedDatabase.Snowflake] = (static (factory, logger) => new SnowflakeDialect(factory, logger), static () => SnowflakeDialect.CreateSnowflakeTraits()),
        [SupportedDatabase.AuroraPostgreSql] = (static (factory, logger) => new PostgreSqlDialect(factory, logger, SupportedDatabase.AuroraPostgreSql), static () => PostgreSqlDialect.CreateAuroraPostgreSqlTraits()),
        [SupportedDatabase.FlatFile] = (static (factory, logger) => new FlatFileDialect(factory, logger), static () => FlatFileDialect.CreateFlatFileTraits()),
        [SupportedDatabase.SybaseASE] = (static (factory, logger) => new SybaseAseDialect(factory, logger), static () => SybaseAseDialect.CreateSybaseAseTraits()),
        [SupportedDatabase.Db2] = (static (factory, logger) => new Db2Dialect(factory, logger), static () => Db2Dialect.CreateDb2Traits()),
        [SupportedDatabase.Informix] = (static (factory, logger) => new InformixDialect(factory, logger), static () => InformixDialect.CreateInformixTraits()),
        [SupportedDatabase.SapHana] = (static (factory, logger) => new HanaDialect(factory, logger), static () => HanaDialect.CreateSapHanaTraits()),
        [SupportedDatabase.InterBase] = (static (factory, logger) => new InterBaseDialect(factory, logger), static () => InterBaseDialect.CreateInterBaseTraits()),
        [SupportedDatabase.Spanner] = (static (factory, logger) => new SpannerDialect(factory, logger), static () => SpannerDialect.CreateSpannerTraits()),
        [SupportedDatabase.Access] = (static (factory, logger) => new AccessDialect(factory, logger), static () => AccessDialect.CreateAccessTraits()),
    };

    public static ISqlDialect CreateDialectForType(
        SupportedDatabase databaseType,
        DbProviderFactory factory,
        ILogger logger) =>
        Registrations.TryGetValue(databaseType, out var registration)
            ? registration.Create(factory, logger)
            : new Sql92Dialect(factory, logger);

    /// <summary>
    /// The instance-free traits of <paramref name="databaseType"/>, declared by the dialect class
    /// that hosts it (REV-039); read through <see cref="DatabaseTraits.For"/>, which builds them once.
    /// </summary>
    internal static DatabaseTraits CreateTraits(SupportedDatabase databaseType) =>
        Registrations.TryGetValue(databaseType, out var registration)
            ? registration.Traits()
            : Sql92Dialect.CreateFallbackTraits();

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
