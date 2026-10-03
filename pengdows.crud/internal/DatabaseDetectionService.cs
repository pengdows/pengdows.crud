// =============================================================================
// FILE: DatabaseDetectionService.cs
// PURPOSE: Detects database products and topology from connections and factories.
//
// AI SUMMARY:
// - Centralized database product detection for all supported providers.
// - Detection methods (in priority order):
//   * DetectFromConnection(): Uses GetSchema("DataSourceInformation")
//   * DetectFromFactory(): Falls back to factory type name matching
//   * DetectProduct(): Tries connection first, then factory
// - DetectTopology(): Identifies LocalDB, embedded modes from connection string.
// - Special handling for FakeDb test infrastructure (factory PretendToBe property; fake
//   connections report their EmulatedProduct through GetSchema).
// - Token matching for:
//   * Schema products: "sql server", "postgres", "mysql", "oracle", etc.
//   * Factory types: "npgsql", "sqlclient", "mysqlconnector", etc.
// - DatabaseTopology record: IsLocalDb, IsEmbedded flags.
// - Firebird embedded detection: checks ServerType, ClientLibrary, path patterns.
// - Used by DatabaseContext to select appropriate SqlDialect.
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.@internal;

/// <summary>
/// Service for detecting database products and topology from connections, factories, and connection strings.
/// Consolidates detection logic for all supported database providers.
/// </summary>
internal static class DatabaseDetectionService
{
    internal const string AuroraMySqlProbe = "SHOW VARIABLES LIKE 'aurora_version'";
    internal const string SingleStoreProbe = "SHOW VARIABLES LIKE 'memsql_version'";
    internal const string SpannerProbe =
        "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'spanner_sys'";
    internal const string AuroraPostgreSqlProbe = "SELECT proname FROM pg_proc WHERE proname = 'aurora_version' LIMIT 1";

    private static readonly (SupportedDatabase Product, string[] Tokens)[] SchemaProductTokens =
    {
        (SupportedDatabase.SqlServer, new[] { "sql server" }),
        (SupportedDatabase.MariaDb, new[] { "mariadb" }),
        (SupportedDatabase.MySql, new[] { "mysql" }),
        (SupportedDatabase.TiDb, new[] { "tidb" }),
        (SupportedDatabase.CockroachDb, new[] { "cockroach" }),
        (SupportedDatabase.YugabyteDb, new[] { "yugabyte" }),
        (SupportedDatabase.Snowflake, new[] { "snowflake" }),
        (SupportedDatabase.PostgreSql, new[] { "postgres", "npgsql" }),
        (SupportedDatabase.Oracle, new[] { "oracle" }),
        (SupportedDatabase.Sqlite, new[] { "sqlite" }),
        (SupportedDatabase.Firebird, new[] { "firebird" }),
        (SupportedDatabase.DuckDB, new[] { "duckdb", "duck db" }),
        (SupportedDatabase.FlatFile, new[] { "flatfile", "flat file" }),
        (SupportedDatabase.SybaseASE, new[] { "adaptive server enterprise", "sybase" }),
        (SupportedDatabase.Db2, new[] { "db2" }),
        (SupportedDatabase.Informix, new[] { "informix" }),
        (SupportedDatabase.SapHana, new[] { "hana" }),
        (SupportedDatabase.InterBase, new[] { "interbase" }),
        (SupportedDatabase.Spanner, new[] { "spanner", "pgadapter" }),
        // CONFIRMED live: GetSchema("DataSourceInformation").DataSourceProductName returns
        // "MS Jet" for a real .accdb via System.Data.OleDb. Deliberately no FactoryTypeTokens
        // entry below — factory.GetType().FullName is "System.Data.OleDb.OleDbFactory" for ANY
        // OLE DB provider (SQLOLEDB, OraOLEDB, etc.), so matching on it would misdetect every
        // other OLE DB connection as Access.
        (SupportedDatabase.Access, new[] { "ms jet" })
    };

    private static readonly (SupportedDatabase Product, string[] Tokens)[] FactoryTypeTokens =
    {
        (SupportedDatabase.SqlServer, new[] { "sqlserver", "system.data.sqlclient", "microsoft.data.sqlclient" }),
        (SupportedDatabase.PostgreSql, new[] { "npgsql", "postgres" }),
        (SupportedDatabase.YugabyteDb, new[] { "yugabyte" }),
        (SupportedDatabase.MySql, new[] { "mysql" }),
        (SupportedDatabase.MariaDb, new[] { "mariadb" }),
        (SupportedDatabase.TiDb, new[] { "tidb" }),
        (SupportedDatabase.Sqlite, new[] { "sqlite" }),
        (SupportedDatabase.Oracle, new[] { "oracle" }),
        (SupportedDatabase.Firebird, new[] { "firebird" }),
        (SupportedDatabase.DuckDB, new[] { "duckdb" }),
        (SupportedDatabase.Snowflake, new[] { "snowflake", "net.snowflake" }),
        (SupportedDatabase.FlatFile, new[] { "flatfile" }),
        (SupportedDatabase.SybaseASE, new[] { "aseclient", "adonetcore" }),
        (SupportedDatabase.Db2, new[] { "db2" }),
        (SupportedDatabase.Informix, new[] { "informix" }),
        (SupportedDatabase.SapHana, new[] { "hana" }),
        (SupportedDatabase.InterBase, new[] { "interbase" }),
        (SupportedDatabase.Spanner, new[] { "spanner", "pgadapter" })
    };

    /// <summary>
    /// Detects database product from a product name string using token matching.
    /// Used as a fallback when only a name string is available.
    /// </summary>
    internal static SupportedDatabase DetectFromName(string name) => Match(name, SchemaProductTokens);

    /// <summary>
    /// Detects database product from connection schema metadata.
    /// Preferred method when connection is available.
    /// </summary>
    public static SupportedDatabase DetectFromConnection(IDbConnection? connection)
        => DetectFromConnectionWithDetail(connection).ResolvedProduct;

    /// <summary>
    /// Same detection as <see cref="DetectFromConnection"/>, but returns the full trail of
    /// probes attempted (and why any of them failed) instead of discarding that evidence.
    /// </summary>
    internal static DatabaseDetectionResult DetectFromConnectionWithDetail(IDbConnection? connection)
        // useAsync: false never awaits anything incomplete, so this completes synchronously.
        => DetectFromConnectionWithDetailCoreAsync(connection, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Asynchronous <see cref="DetectFromConnection"/> for <c>DatabaseContext.CreateAsync</c>
    /// (BP-311): the same probes, each command executed with <c>ExecuteScalarAsync</c>.
    /// </summary>
    internal static async Task<SupportedDatabase> DetectFromConnectionAsync(IDbConnection? connection,
        CancellationToken cancellationToken = default)
        => (await DetectFromConnectionWithDetailCoreAsync(connection, true, cancellationToken).ConfigureAwait(false))
            .ResolvedProduct;

    /// <summary>
    /// The one detection implementation behind the sync and async entry points (BP-311):
    /// <paramref name="useAsync"/> picks <c>ExecuteScalar</c> or <c>ExecuteScalarAsync</c> for each
    /// probe. Cancellation is never recorded as a failed probe; it propagates.
    /// </summary>
    private static async ValueTask<DatabaseDetectionResult> DetectFromConnectionWithDetailCoreAsync(
        IDbConnection? connection, bool useAsync, CancellationToken cancellationToken)
    {
        var attempts = new List<DetectionProbeAttempt>();

        if (connection == null)
        {
            return new DatabaseDetectionResult(SupportedDatabase.Unknown, attempts);
        }

        try
        {
            // Step 1: Schema-based detection — identifies the base product without SQL queries.
            // For fakeDb, GetSchema() returns a DataTable based on EmulatedProduct.
            var detected = SupportedDatabase.Unknown;
            try
            {
                DataTable schema;
                if (connection is DbConnection dbConn)
                {
                    schema = dbConn.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
                }
                else if (connection is ITrackedConnection trackedConn)
                {
                    schema = trackedConn.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
                }
                else
                {
                    schema = new DataTable();
                }

                if (schema.Rows.Count > 0)
                {
                    var productName = schema.Rows[0].Field<string>("DataSourceProductName");
                    var productVersion = schema.Rows[0].Field<string>("DataSourceProductVersion");

                    detected = Match(productName, SchemaProductTokens);

                    // MariaDB reports DataSourceProductName = "MySQL" but its version contains "MariaDB"
                    if (detected == SupportedDatabase.MySql && !string.IsNullOrEmpty(productVersion) &&
                        productVersion.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", true, null));
                        return new DatabaseDetectionResult(SupportedDatabase.MariaDb, attempts);
                    }

                    // TiDB reports DataSourceProductName = "MySQL" but its version contains "TiDB"
                    if (detected == SupportedDatabase.MySql && !string.IsNullOrEmpty(productVersion) &&
                        productVersion.Contains("tidb", StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", true, null));
                        return new DatabaseDetectionResult(SupportedDatabase.TiDb, attempts);
                    }
                }

                attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", true, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Schema unavailable — continue to flavor detection
                attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", false, ex.Message));
            }

            // Step 2: Flavor refinement — runs probes gated on the base product.
            // Aurora MySQL probe only runs for MySql/Unknown base; Aurora PG only for PostgreSql/Unknown.
            // This avoids unnecessary round-trips to SQLite, Oracle, SQL Server, etc.
            cancellationToken.ThrowIfCancellationRequested();
            var (flavor, flavorAttempts) = await DetectFlavorWithDetailCoreAsync(connection, detected, useAsync,
                cancellationToken).ConfigureAwait(false);
            attempts.AddRange(flavorAttempts);
            if (flavor != SupportedDatabase.Unknown)
            {
                return new DatabaseDetectionResult(flavor, attempts);
            }

            if (detected != SupportedDatabase.Unknown)
            {
                return new DatabaseDetectionResult(detected, attempts);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fall back to other detection methods
            attempts.Add(new DetectionProbeAttempt("DetectFromConnection", false, ex.Message));
        }

        return new DatabaseDetectionResult(SupportedDatabase.Unknown, attempts);
    }

    private static async ValueTask<(SupportedDatabase Product, List<DetectionProbeAttempt> Attempts)>
        DetectFlavorWithDetailCoreAsync(
            IDbConnection? connection,
            SupportedDatabase detected,
            bool useAsync,
            CancellationToken cancellationToken)
    {
        var attempts = new List<DetectionProbeAttempt>();

        if (connection == null)
        {
            return (SupportedDatabase.Unknown, attempts);
        }

        try
        {
            // ServerVersion-based checks (no query needed)
            var serverVersion = string.Empty;
            if (connection is DbConnection dbConn)
            {
                serverVersion = dbConn.ServerVersion;
            }
            else if (connection is ITrackedConnection tracked)
            {
                serverVersion = tracked.ServerVersion;
            }
            else if (connection.GetType().GetProperty("ServerVersion") is { } prop)
            {
                serverVersion = prop.GetValue(connection)?.ToString() ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(serverVersion))
            {
                if (serverVersion.Contains("TiDB", StringComparison.OrdinalIgnoreCase))
                {
                    attempts.Add(new DetectionProbeAttempt("ServerVersion", true, null));
                    return (SupportedDatabase.TiDb, attempts);
                }
                if (serverVersion.Contains("-YB-", StringComparison.OrdinalIgnoreCase) ||
                    serverVersion.Contains("Yugabyte", StringComparison.OrdinalIgnoreCase))
                {
                    attempts.Add(new DetectionProbeAttempt("ServerVersion", true, null));
                    return (SupportedDatabase.YugabyteDb, attempts);
                }
                if (serverVersion.Contains("Cockroach", StringComparison.OrdinalIgnoreCase))
                {
                    attempts.Add(new DetectionProbeAttempt("ServerVersion", true, null));
                    return (SupportedDatabase.CockroachDb, attempts);
                }
            }

            // Query-based flavor probes — gated on base product to avoid unnecessary round-trips
            var isMySqlFamily = detected == SupportedDatabase.MySql || detected == SupportedDatabase.Unknown;
            var isPgFamily = detected == SupportedDatabase.PostgreSql || detected == SupportedDatabase.Unknown;

            using var cmd = connection.CreateCommand();

            // Aurora MySQL: SHOW VARIABLES LIKE 'aurora_version' returns the variable's row on Aurora
            // and an empty result elsewhere. @@aurora_version raised "Unknown system variable" on every
            // other server (REV-065: probes must not leave errors in server logs). Any non-string
            // result (e.g. the fakeDb default of int 42) is treated as "not Aurora".
            if (isMySqlFamily)
            {
                try
                {
                    cmd.CommandText = AuroraMySqlProbe;
                    if ((await ExecuteScalarAsync(cmd, useAsync, cancellationToken).ConfigureAwait(false)) is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("AuroraMySqlVersion", true, null));
                        return (SupportedDatabase.AuroraMySql, attempts);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    /* not aurora mysql */
                    attempts.Add(new DetectionProbeAttempt("AuroraMySqlVersion", false, ex.Message));
                }
            }

            // SingleStore (formerly MemSQL): SHOW VARIABLES LIKE 'memsql_version' returns a row on
            // SingleStore and an empty result on MySQL/MariaDB/Aurora MySQL/TiDB, where
            // @@memsql_version raised "Unknown system variable" (REV-065).
            // SELECT VERSION()/@@version report a generic MySQL-compatible version with no distinguishing
            // marker on SingleStore, so this dedicated system-variable probe is required.
            if (isMySqlFamily)
            {
                try
                {
                    cmd.CommandText = SingleStoreProbe;
                    if ((await ExecuteScalarAsync(cmd, useAsync, cancellationToken).ConfigureAwait(false)) is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("SingleStoreVersion", true, null));
                        return (SupportedDatabase.SingleStore, attempts);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    /* not singlestore */
                    attempts.Add(new DetectionProbeAttempt("SingleStoreVersion", false, ex.Message));
                }
            }

            // SELECT version() — safe probe that does not abort the session; used first for PG-family because
            // function-call probes (aurora_version, aurora_version()) can leave a YugabyteDB YSQL
            // connection in an aborted state, silently swallowing subsequent queries.
            // Run this early so the -YB- / Cockroach / TiDB markers are checked before any
            // probe that could corrupt connection state.
            if (isMySqlFamily || isPgFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT version()";
                    var version = (await ExecuteScalarAsync(cmd, useAsync, cancellationToken).ConfigureAwait(false))?.ToString() ?? string.Empty;

                    if (version.Contains("TiDB", StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(new DetectionProbeAttempt("SelectVersion", true, null));
                        return (SupportedDatabase.TiDb, attempts);
                    }
                    if (version.Contains("-YB-", StringComparison.OrdinalIgnoreCase) ||
                        version.Contains("Yugabyte", StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(new DetectionProbeAttempt("SelectVersion", true, null));
                        return (SupportedDatabase.YugabyteDb, attempts);
                    }
                    if (version.Contains("Cockroach", StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(new DetectionProbeAttempt("SelectVersion", true, null));
                        return (SupportedDatabase.CockroachDb, attempts);
                    }

                    attempts.Add(new DetectionProbeAttempt("SelectVersion", true, null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    /* version() not available */
                    attempts.Add(new DetectionProbeAttempt("SelectVersion", false, ex.Message));
                }
            }

            // Spanner PostgreSQL interface discriminator; ordinary PostgreSQL rejects this
            // Spanner-specific setting. Must run before the YugabyteDB pg_settings probe below —
            // on live Spanner this SHOW returns an empty string (verified against a real Spanner
            // Omni + PGAdapter instance), not null, so `is string` (no length check, unlike the
            // other probes here) matches even that empty-string case. Gated on isPgFamily (not
            // just `detected == PostgreSql`) so a connection whose schema-based classification
            // landed on Unknown still gets Spanner-probed.
            if (isPgFamily)
            {
                try
                {
                    // Spanner's own system schema, counted: 1 on Spanner, 0 elsewhere, never an error.
                    // SHOW SPANNER.OPTIMIZER_VERSION raised "unrecognized configuration parameter"
                    // on PostgreSQL, which logs every failed statement by default (REV-065); and
                    // current_setting(..., true) broke PGAdapter's protocol handling (live).
                    cmd.CommandText = SpannerProbe;
                    var spannerSchemas = await ExecuteScalarAsync(cmd, useAsync, cancellationToken).ConfigureAwait(false);
                    if (spannerSchemas is not null and not DBNull &&
                        Convert.ToInt64(spannerSchemas, System.Globalization.CultureInfo.InvariantCulture) > 0)
                    {
                        attempts.Add(new DetectionProbeAttempt("SpannerOptimizerVersion", true, null));
                        return (SupportedDatabase.Spanner, attempts);
                    }
                    attempts.Add(new DetectionProbeAttempt("SpannerOptimizerVersion", true, null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempts.Add(new DetectionProbeAttempt("SpannerOptimizerVersion", false, ex.Message));
                }
            }

            // YugabyteDB fallback: Query pg_settings for a YugabyteDB-only GUC. Runs after
            // SELECT version() as a belt-and-suspenders guard for cases where the version string
            // does not contain the expected markers (e.g. stripped by some proxy/pooler).
            if (isPgFamily)
            {
                try
                {
                    cmd.CommandText =
                        "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1";
                    if ((await ExecuteScalarAsync(cmd, useAsync, cancellationToken).ConfigureAwait(false)) is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", true, null));
                        return (SupportedDatabase.YugabyteDb, attempts);
                    }

                    attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", true, null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    /* pg_settings unavailable — very unusual, continue */
                    attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", false, ex.Message));
                }
            }

            // Aurora PostgreSQL: the aurora_version() function exists only on Aurora. Looked up in
            // pg_proc rather than called: calling it raised "function does not exist" (logged by
            // PostgreSQL) on every other server and could abort a YSQL connection's state (REV-065).
            if (isPgFamily)
            {
                try
                {
                    cmd.CommandText = AuroraPostgreSqlProbe;
                    if ((await ExecuteScalarAsync(cmd, useAsync, cancellationToken).ConfigureAwait(false)) is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", true, null));
                        return (SupportedDatabase.AuroraPostgreSql, attempts);
                    }

                    attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", true, null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    /* not aurora pg */
                    attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", false, ex.Message));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ignore
            attempts.Add(new DetectionProbeAttempt("DetectFlavor", false, ex.Message));
        }

        return (SupportedDatabase.Unknown, attempts);
    }

    // Probe commands run synchronously on the constructor path and asynchronously on
    // DatabaseContext.CreateAsync's (BP-311).
    private static async ValueTask<object?> ExecuteScalarAsync(IDbCommand command, bool useAsync,
        CancellationToken cancellationToken)
    {
        if (useAsync && command is DbCommand dbCommand)
        {
            return await dbCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return command.ExecuteScalar();
    }

    /// <summary>
    /// Detects database product from DbProviderFactory type name.
    /// Fallback method when connection is not available.
    /// </summary>
    public static SupportedDatabase DetectFromFactory(DbProviderFactory? factory)
    {
        if (factory == null)
        {
            return SupportedDatabase.Unknown;
        }

        try
        {
            // Check if this is a fake factory (testing infrastructure)
            if (factory.GetType().Name.Contains("fake", StringComparison.OrdinalIgnoreCase))
            {
                // Try to get PretendToBe property via reflection
                var pretendToBeProperty = factory.GetType().GetProperty("PretendToBe");
                if (pretendToBeProperty != null && pretendToBeProperty.PropertyType == typeof(SupportedDatabase))
                {
                    var value = pretendToBeProperty.GetValue(factory);
                    if (value is SupportedDatabase product)
                    {
                        return product;
                    }
                }
            }

            // Normal detection from factory type name
            var factoryType = factory.GetType();
            return Match(factoryType.FullName ?? factoryType.Name, FactoryTypeTokens);
        }
        catch
        {
            return SupportedDatabase.Unknown;
        }
    }

    /// <summary>
    /// Detects database product trying connection first, then falling back to factory.
    /// This is the primary detection method used by DatabaseContext.
    /// </summary>
    public static SupportedDatabase DetectProduct(IDbConnection? connection, DbProviderFactory? factory)
    {
        // Try connection first (most accurate)
        var fromConnection = DetectFromConnection(connection);
        if (fromConnection != SupportedDatabase.Unknown)
        {
            return fromConnection;
        }

        // Fall back to factory type
        return DetectFromFactory(factory);
    }

    /// <summary>
    /// Asynchronous <see cref="DetectProduct"/> (BP-311): probes with <c>ExecuteScalarAsync</c>.
    /// </summary>
    internal static async Task<SupportedDatabase> DetectProductAsync(IDbConnection? connection,
        DbProviderFactory? factory, CancellationToken cancellationToken = default)
    {
        var fromConnection = await DetectFromConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        return fromConnection != SupportedDatabase.Unknown ? fromConnection : DetectFromFactory(factory);
    }

    /// <summary>
    /// Detects database topology (LocalDB, embedded, etc.) from connection string.
    /// </summary>
    /// <summary>
    /// Returns a row only on Db2 for Linux/Unix/Windows: SYSPROC.ENV_GET_INST_INFO() does not exist
    /// on Db2 for z/OS or Db2 for i.
    /// </summary>
    internal const string Db2LuwProbeSql = "SELECT SERVICE_LEVEL FROM TABLE(SYSPROC.ENV_GET_INST_INFO()) AS INSTANCEINFO";

    /// <summary>
    /// <see cref="DetectTopology(SupportedDatabase, string?)"/> plus facts only a live connection can
    /// tell: whether a Db2 server is Db2 LUW (<see cref="Db2LuwProbeSql"/> returns a row there and
    /// fails on z/OS and IBM i). A failed or missing probe means "not LUW", the safe default.
    /// </summary>
    public static DatabaseTopology DetectTopology(SupportedDatabase product, string? connectionString,
        IDbConnection? connection)
        // useAsync: false never awaits anything incomplete, so this completes synchronously.
        => DetectTopologyCoreAsync(product, connectionString, connection, false, CancellationToken.None)
            .GetAwaiter().GetResult();

    /// <summary>Asynchronous <see cref="DetectTopology(SupportedDatabase, string?, IDbConnection?)"/> (BP-311).</summary>
    internal static Task<DatabaseTopology> DetectTopologyAsync(SupportedDatabase product, string? connectionString,
        IDbConnection? connection, CancellationToken cancellationToken = default)
        => DetectTopologyCoreAsync(product, connectionString, connection, true, cancellationToken).AsTask();

    private static async ValueTask<DatabaseTopology> DetectTopologyCoreAsync(SupportedDatabase product,
        string? connectionString, IDbConnection? connection, bool useAsync, CancellationToken cancellationToken)
    {
        var topology = DetectTopology(product, connectionString);
        if (product != SupportedDatabase.Db2 || connection?.State != ConnectionState.Open)
        {
            return topology;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = Db2LuwProbeSql;
            var result = await ExecuteScalarAsync(command, useAsync, cancellationToken).ConfigureAwait(false);
            return topology with { IsDb2Luw = result is not null and not DBNull };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return topology;
        }
    }

    public static DatabaseTopology DetectTopology(SupportedDatabase product, string? connectionString)
    {
        var isLocalDb = false;
        var isEmbedded = false;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new DatabaseTopology(isLocalDb, isEmbedded);
        }

        var lower = connectionString.ToLowerInvariant();

        // SQL Server LocalDB detection
        if (product == SupportedDatabase.SqlServer)
        {
            isLocalDb = lower.Contains("(localdb)") || lower.Contains("localdb");
        }

        // Firebird embedded detection
        if (product == SupportedDatabase.Firebird)
        {
            try
            {
                var csb = new DbConnectionStringBuilder { ConnectionString = connectionString };

                string GetVal(string key)
                {
                    return csb.ContainsKey(key) ? csb[key]?.ToString() ?? string.Empty : string.Empty;
                }

                var serverType = GetVal("ServerType").ToLowerInvariant();
                var clientLib = GetVal("ClientLibrary").ToLowerInvariant();
                var dataSource = GetVal("DataSource").ToLowerInvariant();
                var database = GetVal("Database").ToLowerInvariant();

                isEmbedded =
                    serverType.Contains("embedded") ||
                    clientLib.Contains("embed") ||
                    (string.IsNullOrWhiteSpace(dataSource) &&
                     !string.IsNullOrWhiteSpace(database) &&
                     (database.Contains('/') || database.Contains('\\') || database.EndsWith(".fdb")));
            }
            catch
            {
                // Heuristic only - don't fail on parse errors
            }
        }

        return new DatabaseTopology(isLocalDb, isEmbedded);
    }

    private static SupportedDatabase Match(string? source, (SupportedDatabase Product, string[] Tokens)[] tokenSets)
    {
        if (string.IsNullOrWhiteSpace(source) || source == "UnknownDb")
        {
            return SupportedDatabase.Unknown;
        }

        foreach (var (product, tokens) in tokenSets)
        {
            foreach (var token in tokens)
            {
                if (source.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return product;
                }
            }
        }

        return SupportedDatabase.Unknown;
    }
}

/// <summary>
/// Represents database topology characteristics (LocalDB, embedded, etc.).
/// </summary>
/// <param name="IsDb2Luw">
/// Db2 for Linux/Unix/Windows (as opposed to Db2 for z/OS or Db2 for i). LUW's default implicit
/// activation deactivates a database when its last connection closes (see Db2Dialect).
/// </param>
internal record DatabaseTopology(bool IsLocalDb, bool IsEmbedded, bool IsDb2Luw = false);