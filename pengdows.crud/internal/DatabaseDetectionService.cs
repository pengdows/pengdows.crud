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
// - Sync (DetectFromConnectionWithDetail) and async (DetectFromConnectionWithDetailAsync) entry
//   points share ALL branching logic via DetectSchemaProduct + DetectFlavorCoreAsync; the only
//   sync/async difference (blocking vs. real-async scalar execution) is isolated behind an
//   executeScalar delegate, so a new probe is added in exactly one place for both paths.
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
        (SupportedDatabase.Spanner, new[] { "spanner", "pgadapter" }),
        (SupportedDatabase.PostgreSql, new[] { "postgres", "npgsql" }),
        (SupportedDatabase.Oracle, new[] { "oracle" }),
        (SupportedDatabase.Sqlite, new[] { "sqlite" }),
        (SupportedDatabase.Firebird, new[] { "firebird" }),
        (SupportedDatabase.DuckDB, new[] { "duckdb", "duck db" }),
        (SupportedDatabase.Db2, new[] { "db2" }),
        (SupportedDatabase.FlatFile, new[] { "flatfile", "flat file" }),
        (SupportedDatabase.Sybase, new[] { "adaptive server enterprise", "sybase" }),
        (SupportedDatabase.Informix, new[] { "informix" }),
        (SupportedDatabase.SapHana, new[] { "hana" })
    };

    private static readonly (SupportedDatabase Product, string[] Tokens)[] FactoryTypeTokens =
    {
        (SupportedDatabase.SqlServer, new[] { "sqlserver", "system.data.sqlclient", "microsoft.data.sqlclient" }),
        (SupportedDatabase.Spanner, new[] { "spanner", "pgadapter" }),
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
        (SupportedDatabase.Db2, new[] { "db2" }),
        (SupportedDatabase.FlatFile, new[] { "flatfile" }),
        (SupportedDatabase.Sybase, new[] { "aseclient", "adonetcore" }),
        (SupportedDatabase.Informix, new[] { "informix" }),
        (SupportedDatabase.SapHana, new[] { "hana" })
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
    /// Async twin of <see cref="DetectFromConnectionWithDetail"/>. Shares every branching/gating
    /// decision with the sync path through <see cref="DetectSchemaProduct"/> and
    /// <see cref="DetectFlavorCoreAsync"/> — the only genuine sync/async difference (the flavor
    /// probes' scalar execution) is isolated behind the <c>executeScalar</c> delegate passed to
    /// <see cref="DetectFlavorCoreAsync"/>, so there is exactly one place that decides which probe
    /// runs for which base product.
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
            var (detected, schemaAttempt, shortCircuit) = DetectSchemaProduct(connection);
            attempts.Add(schemaAttempt);
            if (shortCircuit != null)
            {
                return new DatabaseDetectionResult(shortCircuit.Value, attempts);
            }

            var (flavor, flavorAttempts) = await DetectFlavorCoreAsync(
                connection, detected, ExecuteScalarAsyncCore, cancellationToken).ConfigureAwait(false);
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
    /// Shares <see cref="DetectSchemaProduct"/> and <see cref="DetectFlavorCoreAsync"/> with the
    /// async twin above — see its remarks for how the sync/async split is kept to one place.
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
            var (detected, schemaAttempt, shortCircuit) = DetectSchemaProduct(connection);
            attempts.Add(schemaAttempt);
            if (shortCircuit != null)
            {
                return new DatabaseDetectionResult(shortCircuit.Value, attempts);
            }

            // DetectFlavorCoreAsync only ever awaits ExecuteScalarSyncAsCompletedTask below, which
            // never performs real asynchronous I/O — it wraps a blocking ADO.NET ExecuteScalar()
            // call in an already-completed Task. So this await always completes synchronously,
            // and GetAwaiter().GetResult() never blocks a thread waiting on anything: it just
            // unwraps a result that is already there. This keeps this method genuinely
            // synchronous end-to-end while still sharing all branching logic with the async path.
            var (flavor, flavorAttempts) = DetectFlavorCoreAsync(
                connection, detected, ExecuteScalarSyncAsCompletedTask, CancellationToken.None)
                .GetAwaiter().GetResult();
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
    /// Step 1 of detection: schema-based base-product lookup, shared verbatim by the sync and
    /// async entry points above. There is no true async <c>GetSchema</c> equivalent in ADO.NET, so
    /// this step is always synchronous — it is a fast, typically-cached, no-round-trip metadata
    /// lookup, unlike the flavor probes which are real queries.
    /// </summary>
    private static (SupportedDatabase Detected, DetectionProbeAttempt Attempt, SupportedDatabase? ShortCircuit)
        DetectSchemaProduct(IDbConnection connection)
    {
        // Identifies the base product without SQL queries. For fakeDb, GetSchema() returns a
        // DataTable based on EmulatedProduct.
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
                    return (detected, new DetectionProbeAttempt("SchemaDataSourceInformation", true, null),
                        SupportedDatabase.MariaDb);
                }

                // TiDB reports DataSourceProductName = "MySQL" but its version contains "TiDB"
                if (detected == SupportedDatabase.MySql && !string.IsNullOrEmpty(productVersion) &&
                    productVersion.Contains("tidb", StringComparison.OrdinalIgnoreCase))
                {
                    return (detected, new DetectionProbeAttempt("SchemaDataSourceInformation", true, null),
                        SupportedDatabase.TiDb);
                }
            }

            return (detected, new DetectionProbeAttempt("SchemaDataSourceInformation", true, null), null);
        }
        catch (Exception ex)
        {
            // Schema unavailable — continue to flavor detection
            return (detected, new DetectionProbeAttempt("SchemaDataSourceInformation", false, ex.Message), null);
        }
    }

    /// <summary>
    /// The single, shared flavor-refinement core used by both <see cref="DetectFromConnectionWithDetail"/>
    /// (sync) and <see cref="DetectFromConnectionWithDetailAsync"/> (async). Every gating decision —
    /// which probe runs for which base product, and in what order — lives here exactly once; the
    /// <paramref name="executeScalar"/> delegate is the only seam between the two callers, letting the
    /// sync path run a genuinely blocking <c>ExecuteScalar()</c> and the async path genuinely await
    /// <see cref="DbCommand.ExecuteScalarAsync(CancellationToken)"/> without duplicating the branching
    /// logic itself. This is what closed a real, previously-shipped bug: a probe added to only one of
    /// the two old hand-written twins would compile clean and pass whichever twin's own tests happened
    /// to run, then silently misclassify the database on the other path in production (see
    /// CLAUDE.md's "Adding a New Database" checklist item 15, and
    /// <c>DatabaseDetectionSyncAsyncParityTests</c>) — adding a probe here now takes effect on both
    /// paths by construction, not by remembering to touch two files.
    /// </summary>
    private static async Task<(SupportedDatabase Product, List<DetectionProbeAttempt> Attempts)> DetectFlavorCoreAsync(
        IDbConnection? connection,
        SupportedDatabase detected,
        Func<IDbCommand, CancellationToken, Task<object?>> executeScalar,
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
                    var scalar = await executeScalar(cmd, cancellationToken).ConfigureAwait(false);
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
                    /* not aurora mysql */
                    attempts.Add(new DetectionProbeAttempt("AuroraMySqlVersion", false, ex.Message));
                }
            }

            // SingleStore (formerly MemSQL): @@memsql_version returns a version string (e.g. "9.1.1")
            // on SingleStore, throws "Unknown system variable" on standard MySQL/MariaDB/Aurora MySQL/TiDB.
            // SELECT VERSION()/@@version report a generic MySQL-compatible version with no distinguishing
            // marker on SingleStore, so this dedicated system-variable probe is required.
            if (isMySqlFamily)
            {
                try
                {
                    cmd.CommandText = "SELECT @@memsql_version";
                    var scalar = await executeScalar(cmd, cancellationToken).ConfigureAwait(false);
                    if (scalar is string { Length: > 0 })
                    {
                        attempts.Add(new DetectionProbeAttempt("SingleStoreVersion", true, null));
                        return (SupportedDatabase.SingleStore, attempts);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    /* not singlestore */
                    attempts.Add(new DetectionProbeAttempt("SingleStoreVersion", false, ex.Message));
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
                    var scalar = await executeScalar(cmd, cancellationToken).ConfigureAwait(false);
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
                    /* version() not available */
                    attempts.Add(new DetectionProbeAttempt("SelectVersion", false, ex.Message));
                }
            }

            // Spanner PostgreSQL interface discriminator; ordinary PostgreSQL rejects this
            // Spanner-specific setting. Must run before the YugabyteDB pg_settings probe below —
            // that probe's fakeDb-style fallback semantics aside, on live Spanner this SHOW
            // returns an empty string (verified against a real Spanner Omni + PGAdapter
            // instance), not null, so `is string` (no length check, unlike the other probes here)
            // matches even that empty-string case. Gated on isPgFamily (not just
            // `detected == PostgreSql`) so a connection whose schema-based classification landed
            // on Unknown still gets Spanner-probed — see this method's own doc comment for why
            // that distinction used to matter across two separate implementations.
            if (isPgFamily)
            {
                try
                {
                    cmd.CommandText = "SHOW SPANNER.OPTIMIZER_VERSION";
                    if (await executeScalar(cmd, cancellationToken).ConfigureAwait(false) is string)
                    {
                        attempts.Add(new DetectionProbeAttempt("SpannerOptimizerVersion", true, null));
                        return (SupportedDatabase.Spanner, attempts);
                    }
                    attempts.Add(new DetectionProbeAttempt("SpannerOptimizerVersion", true, null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
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
                    var scalar = await executeScalar(cmd, cancellationToken).ConfigureAwait(false);
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
                    var scalar = await executeScalar(cmd, cancellationToken).ConfigureAwait(false);
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
                    /* not aurora pg */
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
            // Ignore
            attempts.Add(new DetectionProbeAttempt("DetectFlavor", false, ex.Message));
        }

        return (SupportedDatabase.Unknown, attempts);
    }

    /// <summary>
    /// Executes a scalar command asynchronously when the command supports it (the normal ADO.NET
    /// case), falling back to the synchronous path only for an <see cref="IDbCommand"/> that isn't
    /// a real <see cref="DbCommand"/> — which no supported provider's <c>CreateCommand()</c> returns.
    /// Passed to <see cref="DetectFlavorCoreAsync"/> as its <c>executeScalar</c> delegate by the
    /// async entry point.
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

    /// <summary>
    /// Executes a scalar command synchronously (blocking <see cref="IDbCommand.ExecuteScalar"/>)
    /// and wraps the already-available result in a completed <see cref="Task{TResult}"/>. Passed to
    /// <see cref="DetectFlavorCoreAsync"/> as its <c>executeScalar</c> delegate by the sync entry
    /// point — because this delegate never performs real asynchronous I/O, every <c>await</c>
    /// inside <see cref="DetectFlavorCoreAsync"/> completes synchronously when driven by this
    /// delegate, so the sync caller's <c>GetAwaiter().GetResult()</c> never blocks a thread waiting
    /// on anything.
    /// </summary>
    private static Task<object?> ExecuteScalarSyncAsCompletedTask(IDbCommand cmd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(cmd.ExecuteScalar());
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