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

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
/// and Oracle's MySql.Data. MySqlConnector is the preferred provider for
/// new deployments because it supports cleaner pool separation and has shown
/// better behavior under high-concurrency workloads.
/// </para>
/// </remarks>
internal class MySqlDialect : SqlDialect
{
    private const string SqlModeSettingName = "sql_mode";

    private const string RequiredSqlModeFlags =
        "STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES";

    private const string LegacySqlModeFlags = "NO_ZERO_DATE,NO_ZERO_IN_DATE";

    private const string DefaultSqlMode = $"SET SESSION {SqlModeSettingName} = '{RequiredSqlModeFlags},{LegacySqlModeFlags}';";

    // Alias kept for readability at call sites
    private const string ExpectedSqlMode = $"{RequiredSqlModeFlags},{LegacySqlModeFlags}";

    // Use session-persistent variables for read-only enforcement rather than 'SET TRANSACTION'
    // which has "next transaction only" semantics in some variants.
    //
    // Minimum server version requirement:
    //   MySQL:   5.7.20+ (transaction_read_only introduced as a session variable in 5.7.20)
    //            8.0+    (fully supported and preferred)
    //
    // MariaDB note: MariaDB uses tx_read_only (the original pre-5.7.20 MySQL name).
    // The transaction_read_only alias was never adopted by MariaDB — using it raises
    // "Unknown system variable 'transaction_read_only'". MariaDbDialect overrides
    // GetReadOnlySessionSettings() and GetReadOnlyTransactionResetSql() to use tx_read_only.
    //
    // If your deployment targets MySQL < 5.7.20, use
    // 'SET SESSION TRANSACTION READ ONLY' instead (single-transaction semantics only).
    protected const string SetSessionReadOnlySql = "SET SESSION transaction_read_only = 1;";
    protected const string SetSessionReadWriteSql = "SET SESSION transaction_read_only = 0;";

    private static readonly Version UpsertAliasVersionThreshold = new(8, 0, 20);
    private static readonly Version MySqlLegacyModeDeprecationThreshold = new(8, 0, 0);

    private const int MaxPreparedStatementCountErrorCode = 1461;
    private const string MaxPreparedStatementCountToken = "max_prepared_stmt_count";
    private const string PreferredProviderWarning =
        "MySql.Data is supported, but MySqlConnector is the preferred MySQL provider for pengdows.crud. " +
        "MySqlConnector provides better read/write pool separation support and has shown better behavior under high concurrency.";

    private const int DefaultMySqlConnectionTimeout = 15;

    private static readonly string[] ConnectionTimeoutKeys =
    {
        "Connection Timeout", "ConnectionTimeout", "Connect Timeout"
    };

    private string? _sessionSettings;
    protected readonly bool _isMySqlConnector;
    private readonly SupportedDatabase _flavor;
    private volatile bool _prepareDisabledByServerLimit;

    internal MySqlDialect(DbProviderFactory factory, ILogger logger, SupportedDatabase flavor = SupportedDatabase.MySql)
        : this(factory, logger,
            (factory.GetType().Namespace ?? string.Empty)
            .Contains("MySqlConnector", StringComparison.OrdinalIgnoreCase),
            flavor)
    {
    }

    internal MySqlDialect(DbProviderFactory factory, ILogger logger, bool isMySqlConnector, SupportedDatabase flavor = SupportedDatabase.MySql)
        : base(factory, logger)
    {
        _isMySqlConnector = isMySqlConnector;
        _flavor = flavor;

        if (ShouldWarnAboutMySqlDataProvider(factory))
        {
            Logger.LogWarning(PreferredProviderWarning);
        }
    }

    public override SupportedDatabase DatabaseType => _flavor;
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

    // MySqlConnector uses binary-protocol parameters (no SQL text substitution), so
    // prepared statements are safe and correct — and avoid server-side backslash processing
    // that can corrupt string values containing escape sequences when using text protocol.
    // Oracle MySql.Data defaults OFF to avoid max_prepared_stmt_count exhaustion on older servers.
    // ShouldDisablePrepareOn() guards against error 1461 on either path.
    public override bool PrepareStatements => _isMySqlConnector;
    public override bool SupportsReadOnlyTransactions => true;

    // Once MySQL error 1461 fires, veto ALL future prepare attempts — including ForceManualPrepare.
    // Retrying after exhaustion only compounds the problem.
    public override bool IsPrepareExhausted => _prepareDisabledByServerLimit;

    public override bool SupportsNamespaces => true;

    // MySQL uses LIMIT/OFFSET only — it does not support SQL:2008 OFFSET/FETCH NEXT syntax.
    // MariaDB (a separate fork) added OFFSET/FETCH in 10.6+ and overrides this property.
    public override bool SupportsOffsetFetch => false;

    public override bool SupportsOnDuplicateKey => true; // Available since MySQL 4.1 (2004) - safe to assume
    public override bool SupportsMerge => false;
    public override bool SupportsSavepoints => true; // Available since MySQL 5.0.3 (2005)
    public override bool SupportsJsonTypes => IsInitialized && IsVersionAtLeast(5, 7, 8);
    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;

    public override string GetLastInsertedIdQuery()
    {
        return "SELECT LAST_INSERT_ID()";
    }

    /// <summary>
    /// MySqlConnector 2.x deliberately does not support AllowMultipleStatements, so the
    /// CompoundStatement plan cannot work. Use ReaderInsertedId instead: execute the INSERT
    /// as a reader and read LastInsertedId from the MySqlDataReader OK-packet property — no
    /// second round-trip and no multi-statement support required.
    ///
    /// Oracle MySql.Data DOES support Allow Multiple Statements; keep CompoundStatement for
    /// that provider so nothing regresses.
    /// </summary>
    public override GeneratedKeyPlan GetGeneratedKeyPlan() =>
        _isMySqlConnector ? GeneratedKeyPlan.ReaderInsertedId : GeneratedKeyPlan.CompoundStatement;

    /// <summary>
    /// Reads the generated auto-increment key from the DbCommand after INSERT using reflection.
    /// MySqlCommand.LastInsertedId is populated from the MySQL OK packet — no extra query needed.
    /// Returns null when the command is null or the property is absent (fakeDb, non-MySqlConnector providers).
    /// PropertyInfo is cached per command type to eliminate per-call reflection overhead on
    /// high-frequency INSERT workloads (e.g. bulk create loops).
    /// </summary>
    // Keyed by concrete command type so that mixed-type scenarios (e.g. fakeDbCommand in tests
    // alongside a real MySqlCommand in prod) each resolve their own PropertyInfo independently.
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> s_lastInsertedIdProps = new();

    public override object? GetLastInsertedIdFromCommand(DbCommand? command)
    {
        if (command is null)
        {
            return null;
        }

        var prop = s_lastInsertedIdProps.GetOrAdd(command.GetType(),
            static t => t.GetProperty("LastInsertedId"));
        return prop?.GetValue(command);
    }

    /// <summary>
    /// SQL suffix appended to INSERT for compound execution.
    /// Requires Allow Multiple Statements=true (Oracle MySql.Data only).
    /// </summary>
    public override string GetCompoundInsertIdSuffix() => "; SELECT LAST_INSERT_ID()";

    public override string GetVersionQuery()
    {
        return "SELECT VERSION()";
    }

    public override bool ShouldDisablePrepareOn(Exception ex)
    {
        if (base.ShouldDisablePrepareOn(ex))
        {
            return true;
        }

        if (!IsMaxPreparedStatementLimit(ex))
        {
            return false;
        }

        if (!_prepareDisabledByServerLimit)
        {
            _prepareDisabledByServerLimit = true;
            Logger.LogWarning(ex,
                "Disabling prepare for MySQL dialect after max_prepared_stmt_count limit was reached.");
        }

        return true;
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
                Logger.LogInformation(
                    "MySQL session settings: already compliant; enforcing baseline on every checkout");
            }
        }

        return productInfo;
    }

    public override string GetFinalSessionSettings(bool readOnly)
    {
        // 1 RTT / 1 Command Optimization: Combine sql_mode and session-persistent read-only intent.
        var baseline = GetBaseSessionSettings();
        var intent = readOnly ? SetSessionReadOnlySql : SetSessionReadWriteSql;

        return baseline.TrimEnd(';') + "; " + intent;
    }

    private SessionSettingsResult GetMySqlSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                var version = ProductInfo.ParsedVersion;
                var useLegacyModes = version == null ||
                                     _flavor == SupportedDatabase.MariaDb ||
                                     version < MySqlLegacyModeDeprecationThreshold;

                var flags = useLegacyModes
                    ? $"{RequiredSqlModeFlags},{LegacySqlModeFlags}"
                    : RequiredSqlModeFlags;

                // Overwrite-only policy: Always set the full sql_mode to our standard baseline.
                // This is safer than delta interrogation across a pooled connection lifetime.
                var script = $"SET SESSION {SqlModeSettingName} = '{flags}';";

                return new SessionSettingsResult(script, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SqlModeSettingName] = flags
                }, false);
            },
            () => new SessionSettingsResult(
                DefaultSqlMode,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sql_mode"] = "unknown"
                },
                true),
            "Failed to configure MySQL session settings, applying default settings");
    }

    internal override string PrepareConnectionStringForDataSource(string connectionString, bool readOnly = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            var builder = Factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
            builder.ConnectionString = connectionString;

            if (_isMySqlConnector)
            {
                // PERFORMANCE: Disable driver-level connection reset to avoid an extra RTT per lease.
                // Since pengdows.crud enforces a session baseline on every lease, driver-level reset
                // is redundant and expensive.
                const string resetKey = "ConnectionReset";
                if (!builder.ContainsKey(resetKey))
                {
                    builder[resetKey] = false;
                    Logger.LogDebug("Injecting ConnectionReset=false for MySqlConnector to optimize lease performance (1 RTT strategy)");
                }
                // NOTE: AllowMultipleStatements is deliberately NOT injected for MySqlConnector.
                // MySqlConnector 2.x does not support this option (it is not a known connection string
                // property). The ReaderInsertedId plan reads LastInsertedId from the MySqlDataReader
                // OK packet instead, requiring no multi-statement support.
            }
            else
            {
                // Oracle MySql.Data: enable multi-statement execution via its own key name.
                const string multiStmtKey = "Allow Multiple Statements";
                if (!builder.ContainsKey(multiStmtKey))
                {
                    builder[multiStmtKey] = true;
                    Logger.LogDebug("Injecting 'Allow Multiple Statements=true' for Oracle MySql.Data to support CompoundStatement generated-key plan");
                }
            }

            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    private static bool IsMaxPreparedStatementLimit(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current.Message.Contains(MaxPreparedStatementCountToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (TryGetProviderErrorCode(current) == MaxPreparedStatementCountErrorCode)
            {
                return true;
            }
        }

        return false;
    }

    private static int? TryGetProviderErrorCode(Exception ex)
    {
        var numberProperty = ex.GetType().GetProperty("Number");
        if (numberProperty?.PropertyType == typeof(int) && numberProperty.GetValue(ex) is int number)
        {
            return number;
        }

        return null;
    }

    public override string GetBaseSessionSettings()
    {
        return string.IsNullOrWhiteSpace(_sessionSettings) ? DefaultSqlMode : _sessionSettings;
    }

    public override string GetReadOnlySessionSettings()
    {
        return SetSessionReadOnlySql;
    }

    internal override string? GetReadOnlyTransactionResetSql()
    {
        return SetSessionReadWriteSql;
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
        TryExecuteReadOnlySql(transaction, SetSessionReadOnlySql, "MySQL");
    }

    public override ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        return TryExecuteReadOnlySqlAsync(transaction, SetSessionReadOnlySql, "MySQL", cancellationToken);
    }

    // Connection pooling properties for MySQL (provider-aware)
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => _isMySqlConnector ? "MinimumPoolSize" : "Min Pool Size";
    public override string? MaxPoolSizeSettingName => _isMySqlConnector ? "MaximumPoolSize" : "Max Pool Size";

    public override string? ApplicationNameSettingName =>
        _isMySqlConnector ? "Application Name" : null;

    private static bool ShouldWarnAboutMySqlDataProvider(DbProviderFactory factory)
    {
        var factoryType = factory.GetType();
        var typeNamespace = factoryType.Namespace ?? string.Empty;
        var assemblyName = factoryType.Assembly.GetName().Name ?? string.Empty;

        return typeNamespace.Contains("MySql.Data", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("MySql.Data", StringComparison.OrdinalIgnoreCase);
    }

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
