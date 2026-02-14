// =============================================================================
// FILE: MySqlDialect.cs
// PURPOSE: MySQL specific dialect implementation.
//
// AI SUMMARY:
// - Supports MySQL 5.7+ and 8.0+ with version-specific features.
// - Key features:
//   * INSERT ... ON DUPLICATE KEY UPDATE for upserts
//   * Parameter marker: @ (at sign)
//   * Identifier quoting: "name" (with ANSI_QUOTES mode)
//   * Max parameters: 65535 (theoretical max)
//   * Prepared statements enabled
// - Session settings: STRICT_ALL_TABLES, ANSI_QUOTES mode for SQL standard.
// - MySQL 8.0.20+ uses new alias syntax for ON DUPLICATE KEY UPDATE.
// - Detects MySqlConnector vs Oracle's MySql.Data provider.
// - LAST_INSERT_ID() for returning generated IDs.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// MySQL dialect with ANSI SQL mode configuration.
/// </summary>
/// <remarks>
/// <para>
/// Supports MySQL 5.7 and 8.0+ with automatic version detection.
/// Enforces ANSI-compatible SQL mode for consistent behavior.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses INSERT ... ON DUPLICATE KEY UPDATE.
/// MySQL 8.0.20+ uses the new alias syntax.
/// </para>
/// <para>
/// <strong>Providers:</strong> Supports both MySqlConnector (recommended)
/// and Oracle's MySql.Data.
/// </para>
/// </remarks>
internal class MySqlDialect : SqlDialect
{
    private const string SqlModeSettingName = "sql_mode";

    private const string RequiredSqlModeFlags =
        "STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE," +
        "ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES";

    private const string DefaultSqlMode = $"SET SESSION {SqlModeSettingName} = '{RequiredSqlModeFlags}';";

    // Alias kept for readability at call sites
    private const string ExpectedSqlMode = RequiredSqlModeFlags;

    protected const string SetSessionTransactionReadOnlySql = "SET SESSION TRANSACTION READ ONLY;";

    private static readonly Version UpsertAliasVersionThreshold = new(8, 0, 20);

    private const int DefaultMySqlConnectionTimeout = 15;

    private static readonly string[] ConnectionTimeoutKeys =
    {
        "Connection Timeout", "ConnectionTimeout", "Connect Timeout"
    };

    private string? _sessionSettings;
    private readonly bool _isMySqlConnector;

    internal MySqlDialect(DbProviderFactory factory, ILogger logger)
        : this(factory, logger,
            (factory.GetType().Namespace ?? string.Empty)
                .Contains("MySqlConnector", StringComparison.OrdinalIgnoreCase))
    {
    }

    internal MySqlDialect(DbProviderFactory factory, ILogger logger, bool isMySqlConnector)
        : base(factory, logger)
    {
        _isMySqlConnector = isMySqlConnector;
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.MySql;
    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";
    public override string ParameterMarker => "@";

    public override bool SupportsNamedParameters => true;

    // IMMUTABLE: MySQL theoretical maximum parameter limit - do not change without extensive testing
    public override int MaxParameterLimit => 65535;

    // IMMUTABLE: MySQL output parameter limit - do not change without extensive testing
    public override int MaxOutputParameters => 65535;

    // IMMUTABLE: MySQL identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 64;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;

    // MySQL benefits from server-side prepared statements
    public override bool PrepareStatements => true;

    public override bool SupportsNamespaces => true;

    public override bool SupportsOnDuplicateKey => true; // Available since MySQL 4.1 (2004) - safe to assume
    public override bool SupportsMerge => false;
    public override bool SupportsSavepoints => true; // Available since MySQL 5.0.3 (2005)
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 5;
    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;

    public override string GetLastInsertedIdQuery()
    {
        return "SELECT LAST_INSERT_ID()";
    }

    public override string GetVersionQuery()
    {
        return "SELECT VERSION()";
    }

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);

        // Check and cache MySQL session settings during initialization
        if (_sessionSettings == null)
        {
            var result = GetMySqlSessionSettings(connection);
            _sessionSettings = result.Settings;

            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying MySQL session settings on first connect:\n{Settings}",
                    _sessionSettings);
            }
            else
            {
                Logger.LogInformation("MySQL session settings: no changes required (already compliant)");
            }
        }

        return productInfo;
    }

    private SessionSettingsResult GetMySqlSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT @@{SqlModeSettingName}";

                var currentSqlMode = cmd.ExecuteScalar()?.ToString() ?? string.Empty;
                var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SqlModeSettingName] = currentSqlMode
                };

                var script = SqlModeContainsAll(currentSqlMode, ExpectedSqlMode)
                    ? string.Empty
                    : DefaultSqlMode;

                return new SessionSettingsResult(script, snapshot, false);
            },
            () => new SessionSettingsResult(
                DefaultSqlMode,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sql_mode"] = "unknown"
                },
                true),
            "Failed to check MySQL session settings, applying default settings");
    }

    private static bool SqlModeContainsAll(string currentMode, string expectedMode)
    {
        var currentModes = currentMode.Split(',').Select(m => m.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedModes = expectedMode.Split(',').Select(m => m.Trim());

        return expectedModes.All(expectedModes => currentModes.Contains(expectedModes));
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        var baseSettings = string.IsNullOrWhiteSpace(_sessionSettings) ? DefaultSqlMode : _sessionSettings;

        if (readOnly)
        {
            return $"{baseSettings}\nSET SESSION TRANSACTION READ ONLY;";
        }

        // Explicitly reset read-only state: provider-pooled connections retain SESSION
        // variables across checkouts, so a prior read connection's
        // "SET SESSION TRANSACTION READ ONLY" persists until cleared.
        return $"{baseSettings}\nSET SESSION TRANSACTION READ WRITE;";
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return _sessionSettings ?? DefaultSqlMode;
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql92;
        }

        return version.Major switch
        {
            >= 8 => SqlStandardLevel.Sql2008,
            >= 6 => SqlStandardLevel.Sql2003,
            >= 5 => SqlStandardLevel.Sql99,
            _ => SqlStandardLevel.Sql92
        };
    }

    public override string UpsertIncomingColumn(string columnName)
    {
        var alias = UpsertIncomingAlias;
        if (!string.IsNullOrEmpty(alias))
        {
            return $"{WrapObjectName(alias)}.{WrapObjectName(columnName)}";
        }

        return $"VALUES({WrapObjectName(columnName)})";
    }

    public override string? UpsertIncomingAlias => UseUpsertAlias ? "incoming" : null;

    private bool UseUpsertAlias =>
        IsInitialized &&
        ProductInfo.ParsedVersion is { } version &&
        version >= UpsertAliasVersionThreshold;

    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        TryExecuteReadOnlySql(transaction, SetSessionTransactionReadOnlySql, "MySQL");
    }

    // Connection pooling properties for MySQL (provider-aware)
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => _isMySqlConnector ? "MinimumPoolSize" : "Min Pool Size";
    public override string? MaxPoolSizeSettingName => _isMySqlConnector ? "MaximumPoolSize" : "Max Pool Size";
    public override string? ApplicationNameSettingName =>
        _isMySqlConnector ? "Application Name" : null;

    internal override string GetReadOnlyConnectionString(string connectionString)
    {
        if (_isMySqlConnector)
        {
            // MySqlConnector supports ApplicationName; pool split handled by
            // ApplyApplicationNameSuffix in BuildReaderConnectionString.
            return connectionString;
        }

        // Oracle MySql.Data does not support ApplicationName.
        // Use Connection Timeout delta (+1s) to create a distinct connection string
        // that forces a separate connection pool for read-only connections.
        return ApplyConnectionTimeoutDelta(connectionString);
    }

    private string ApplyConnectionTimeoutDelta(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            var builder = Factory.CreateConnectionStringBuilder()
                          ?? new DbConnectionStringBuilder();
            builder.ConnectionString = connectionString;

            var currentTimeout = DefaultMySqlConnectionTimeout;
            foreach (var key in ConnectionTimeoutKeys)
            {
                if (builder.TryGetValue(key, out var value) &&
                    int.TryParse(value?.ToString(), out var parsed) &&
                    parsed > 0)
                {
                    currentTimeout = parsed;
                    break;
                }
            }

            builder["Connection Timeout"] = currentTimeout + 1;
            return builder.ConnectionString;
        }
        catch
        {
            // Fallback: append directly
            return $"{connectionString};Connection Timeout={DefaultMySqlConnectionTimeout + 1}";
        }
    }
}