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
// - Special handling for FakeDb test infrastructure (EmulatedProduct property).
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
        (SupportedDatabase.DuckDB, new[] { "duckdb", "duck db" })
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
        (SupportedDatabase.Snowflake, new[] { "snowflake", "net.snowflake" })
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
    /// Async twin of <see cref="DetectFromConnection"/>. Unlike the sync version, the
    /// round-trip flavor probes (Aurora/TiDB/Yugabyte/Cockroach) genuinely await I/O via
    /// <see cref="DbCommand.ExecuteScalarAsync(CancellationToken)"/> instead of blocking.
    /// The schema-based base-product lookup still uses the synchronous <c>GetSchema</c> — ADO.NET
    /// has no true async equivalent for that call.
    /// </summary>
    public static async Task<SupportedDatabase> DetectFromConnectionAsync(
        IDbConnection? connection, CancellationToken cancellationToken = default)
    {
        var result = await DetectFromConnectionWithDetailAsync(connection, cancellationToken).ConfigureAwait(false);
        return result.ResolvedProduct;
    }

    /// <summary>
    /// Async twin of <see cref="DetectFromConnectionWithDetail"/>.
    /// </summary>
    internal static async Task<DatabaseDetectionResult> DetectFromConnectionWithDetailAsync(
        IDbConnection? connection, CancellationToken cancellationToken = default)
    {
        var attempts = new List<DetectionProbeAttempt>();

        if (connection == null)
        {
            return new DatabaseDetectionResult(SupportedDatabase.Unknown, attempts);
        }

        try
        {
            // Step 1: Schema-based detection — no true async GetSchema equivalent in ADO.NET,
            // so this step stays synchronous. It is a fast, typically-cached, no-round-trip
            // metadata lookup, unlike the flavor probes below which are real queries.
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

                    if (detected == SupportedDatabase.MySql && !string.IsNullOrEmpty(productVersion) &&
                        productVersion.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", true, null));
                        return new DatabaseDetectionResult(SupportedDatabase.MariaDb, attempts);
                    }

                    if (detected == SupportedDatabase.MySql && !string.IsNullOrEmpty(productVersion) &&
                        productVersion.Contains("tidb", StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", true, null));
                        return new DatabaseDetectionResult(SupportedDatabase.TiDb, attempts);
                    }
                }

                attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", true, null));
            }
            catch (Exception ex)
            {
                attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", false, ex.Message));
            }

            var (flavor, flavorAttempts) = await DetectFlavorWithDetailAsync(connection, detected, cancellationToken)
                .ConfigureAwait(false);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            attempts.Add(new DetectionProbeAttempt("DetectFromConnection", false, ex.Message));
        }

        return new DatabaseDetectionResult(SupportedDatabase.Unknown, attempts);
    }

    /// <summary>
    /// Same detection as <see cref="DetectFromConnection"/>, but returns the full trail of
    /// probes attempted (and why any of them failed) instead of discarding that evidence.
    /// </summary>
    internal static DatabaseDetectionResult DetectFromConnectionWithDetail(IDbConnection? connection)
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
            catch (Exception ex)
            {
                // Schema unavailable — continue to flavor detection
                attempts.Add(new DetectionProbeAttempt("SchemaDataSourceInformation", false, ex.Message));
            }

            // Step 2: Flavor refinement — runs probes gated on the base product.
            // Aurora MySQL probe only runs for MySql/Unknown base; Aurora PG only for PostgreSql/Unknown.
            // This avoids unnecessary round-trips to SQLite, Oracle, SQL Server, etc.
            var (flavor, flavorAttempts) = DetectFlavorWithDetail(connection, detected);
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
        catch (Exception ex)
        {
            // Fall back to other detection methods
            attempts.Add(new DetectionProbeAttempt("DetectFromConnection", false, ex.Message));
        }

        return new DatabaseDetectionResult(SupportedDatabase.Unknown, attempts);
    }

    /// <summary>
    /// Async twin of <see cref="DetectFlavorWithDetail"/>. The round-trip probe queries use
    /// <see cref="DbCommand.ExecuteScalarAsync(CancellationToken)"/> via <see cref="ExecuteScalarAsyncCore"/>
    /// instead of blocking <c>ExecuteScalar()</c>. <c>ServerVersion</c> access is a plain property
    /// read (no I/O), so it stays synchronous like the sync version.
    /// </summary>
    private static async Task<(SupportedDatabase Product, List<DetectionProbeAttempt> Attempts)> DetectFlavorWithDetailAsync(
        IDbConnection? connection,
        SupportedDatabase detected,
        CancellationToken cancellationToken)
    {
        var attempts = new List<DetectionProbeAttempt>();

        if (connection == null)
        {
            return (SupportedDatabase.Unknown, attempts);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            var isMySqlFamily = detected == SupportedDatabase.MySql || detected == SupportedDatabase.Unknown;
            var isPgFamily = detected == SupportedDatabase.PostgreSql || detected == SupportedDatabase.Unknown;

            using var cmd = connection.CreateCommand();

            if (isMySqlFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT @@aurora_version";
                    var scalar = await ExecuteScalarAsyncCore(cmd, cancellationToken).ConfigureAwait(false);
                    if (scalar is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("AuroraMySqlVersion", true, null));
                        return (SupportedDatabase.AuroraMySql, attempts);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempts.Add(new DetectionProbeAttempt("AuroraMySqlVersion", false, ex.Message));
                }
            }

            if (isMySqlFamily || isPgFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT version()";
                    var scalar = await ExecuteScalarAsyncCore(cmd, cancellationToken).ConfigureAwait(false);
                    var version = scalar?.ToString() ?? string.Empty;

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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempts.Add(new DetectionProbeAttempt("SelectVersion", false, ex.Message));
                }
            }

            if (isPgFamily)
            {
                try
                {
                    cmd.CommandText =
                        "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1";
                    var scalar = await ExecuteScalarAsyncCore(cmd, cancellationToken).ConfigureAwait(false);
                    if (scalar is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", true, null));
                        return (SupportedDatabase.YugabyteDb, attempts);
                    }

                    attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", true, null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", false, ex.Message));
                }
            }

            if (isPgFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT aurora_version()";
                    var scalar = await ExecuteScalarAsyncCore(cmd, cancellationToken).ConfigureAwait(false);
                    if (scalar is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", true, null));
                        return (SupportedDatabase.AuroraPostgreSql, attempts);
                    }

                    attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", true, null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", false, ex.Message));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            attempts.Add(new DetectionProbeAttempt("DetectFlavor", false, ex.Message));
        }

        return (SupportedDatabase.Unknown, attempts);
    }

    /// <summary>
    /// Executes a scalar command asynchronously when the command supports it (the normal ADO.NET
    /// case), falling back to the synchronous path only for an <see cref="IDbCommand"/> that isn't
    /// a real <see cref="DbCommand"/> — which no supported provider's <c>CreateCommand()</c> returns.
    /// </summary>
    private static Task<object?> ExecuteScalarAsyncCore(IDbCommand cmd, CancellationToken cancellationToken)
    {
        if (cmd is DbCommand dbCommand)
        {
            return dbCommand.ExecuteScalarAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(cmd.ExecuteScalar());
    }

    private static (SupportedDatabase Product, List<DetectionProbeAttempt> Attempts) DetectFlavorWithDetail(
        IDbConnection? connection,
        SupportedDatabase detected)
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

            // Aurora MySQL: @@aurora_version returns a version string (e.g. "2.09.1") on Aurora,
            // throws "Unknown system variable" on standard MySQL. Any non-string result (e.g. the
            // fakeDb default of int 42) is treated as "not Aurora".
            if (isMySqlFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT @@aurora_version";
                    if (cmd.ExecuteScalar() is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("AuroraMySqlVersion", true, null));
                        return (SupportedDatabase.AuroraMySql, attempts);
                    }
                }
                catch (Exception ex)
                {
                    /* not aurora mysql */
                    attempts.Add(new DetectionProbeAttempt("AuroraMySqlVersion", false, ex.Message));
                }
            }

            // SELECT version() — safe probe that never throws; used first for PG-family because
            // function-call probes (aurora_version, aurora_version()) can leave a YugabyteDB YSQL
            // connection in an aborted state, silently swallowing subsequent queries.
            // Run this early so the -YB- / Cockroach / TiDB markers are checked before any
            // probe that could corrupt connection state.
            if (isMySqlFamily || isPgFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT version()";
                    var version = cmd.ExecuteScalar()?.ToString() ?? string.Empty;

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
                catch (Exception ex)
                {
                    /* version() not available */
                    attempts.Add(new DetectionProbeAttempt("SelectVersion", false, ex.Message));
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
                    if (cmd.ExecuteScalar() is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", true, null));
                        return (SupportedDatabase.YugabyteDb, attempts);
                    }

                    attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", true, null));
                }
                catch (Exception ex)
                {
                    /* pg_settings unavailable — very unusual, continue */
                    attempts.Add(new DetectionProbeAttempt("YugabytePgSettings", false, ex.Message));
                }
            }

            // Aurora PostgreSQL: aurora_version() returns a version string on Aurora,
            // throws "function does not exist" on standard PostgreSQL. Runs last because it
            // can leave a YSQL connection in an aborted state (already handled above).
            if (isPgFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT aurora_version()";
                    if (cmd.ExecuteScalar() is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", true, null));
                        return (SupportedDatabase.AuroraPostgreSql, attempts);
                    }

                    attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", true, null));
                }
                catch (Exception ex)
                {
                    /* not aurora pg */
                    attempts.Add(new DetectionProbeAttempt("AuroraPostgreSqlVersion", false, ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            // Ignore
            attempts.Add(new DetectionProbeAttempt("DetectFlavor", false, ex.Message));
        }

        return (SupportedDatabase.Unknown, attempts);
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
    /// Async twin of <see cref="DetectProduct"/>.
    /// </summary>
    public static async Task<SupportedDatabase> DetectProductAsync(
        IDbConnection? connection, DbProviderFactory? factory, CancellationToken cancellationToken = default)
    {
        var fromConnection = await DetectFromConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (fromConnection != SupportedDatabase.Unknown)
        {
            return fromConnection;
        }

        return DetectFromFactory(factory);
    }

    /// <summary>
    /// Detects database topology (LocalDB, embedded, etc.) from connection string.
    /// </summary>
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
internal record DatabaseTopology(bool IsLocalDb, bool IsEmbedded);