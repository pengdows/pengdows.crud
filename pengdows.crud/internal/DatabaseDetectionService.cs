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
    {
        if (connection == null)
        {
            return SupportedDatabase.Unknown;
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
                        return SupportedDatabase.MariaDb;
                    }

                    // TiDB reports DataSourceProductName = "MySQL" but its version contains "TiDB"
                    if (detected == SupportedDatabase.MySql && !string.IsNullOrEmpty(productVersion) &&
                        productVersion.Contains("tidb", StringComparison.OrdinalIgnoreCase))
                    {
                        return SupportedDatabase.TiDb;
                    }
                }
            }
            catch
            {
                // Schema unavailable — continue to flavor detection
            }

            // Step 2: Flavor refinement — runs probes gated on the base product.
            // Aurora MySQL probe only runs for MySql/Unknown base; Aurora PG only for PostgreSql/Unknown.
            // This avoids unnecessary round-trips to SQLite, Oracle, SQL Server, etc.
            var flavor = DetectFlavor(connection, detected);
            if (flavor != SupportedDatabase.Unknown)
            {
                return flavor;
            }

            if (detected != SupportedDatabase.Unknown)
            {
                return detected;
            }
        }
        catch
        {
            // Fall back to other detection methods
        }

        return SupportedDatabase.Unknown;
    }

    private static SupportedDatabase DetectFlavor(IDbConnection? connection, SupportedDatabase detected)
    {
        if (connection == null)
        {
            return SupportedDatabase.Unknown;
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
                    return SupportedDatabase.TiDb;
                }
                if (serverVersion.Contains("-YB-", StringComparison.OrdinalIgnoreCase) ||
                    serverVersion.Contains("Yugabyte", StringComparison.OrdinalIgnoreCase))
                {
                    return SupportedDatabase.YugabyteDb;
                }
                if (serverVersion.Contains("Cockroach", StringComparison.OrdinalIgnoreCase))
                {
                    return SupportedDatabase.CockroachDb;
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
                        return SupportedDatabase.AuroraMySql;
                    }
                }
                catch
                {
                    /* not aurora mysql */
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
                        return SupportedDatabase.TiDb;
                    }
                    if (version.Contains("-YB-", StringComparison.OrdinalIgnoreCase) ||
                        version.Contains("Yugabyte", StringComparison.OrdinalIgnoreCase))
                    {
                        return SupportedDatabase.YugabyteDb;
                    }
                    if (version.Contains("Cockroach", StringComparison.OrdinalIgnoreCase))
                    {
                        return SupportedDatabase.CockroachDb;
                    }
                }
                catch
                {
                    /* version() not available */
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
                        return SupportedDatabase.YugabyteDb;
                    }
                }
                catch
                {
                    /* pg_settings unavailable — very unusual, continue */
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
                        return SupportedDatabase.AuroraPostgreSql;
                    }
                }
                catch
                {
                    /* not aurora pg */
                }
            }
        }
        catch
        {
            // Ignore
        }

        return SupportedDatabase.Unknown;
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