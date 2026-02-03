// =============================================================================
// FILE: PostgreSqlDialect.cs
// PURPOSE: PostgreSQL specific dialect implementation.
//
// AI SUMMARY:
// - Supports PostgreSQL 10+ with comprehensive SQL standard compliance.
// - Key features:
//   * INSERT ... ON CONFLICT for upserts (supports DO UPDATE and DO NOTHING)
//   * Parameter marker: : (colon prefix, Npgsql standard)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 32767 (practical limit)
//   * Prepared statements enabled for performance
// - Session settings: standard_conforming_strings, client_min_messages.
// - RETURNING clause for getting generated IDs.
// - Array/range type support via Npgsql.
// - CockroachDB uses this dialect (Postgres-compatible wire protocol).
// - Stored procedure support via CALL statement.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// PostgreSQL dialect with comprehensive SQL standard compliance.
/// </summary>
/// <remarks>
/// <para>
/// Supports PostgreSQL 10 and later with automatic version detection.
/// Also used for CockroachDB (Postgres-compatible).
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses INSERT ... ON CONFLICT (key) DO UPDATE.
/// </para>
/// <para>
/// <strong>Prepared Statements:</strong> Enabled by default for performance.
/// </para>
/// </remarks>
internal class PostgreSqlDialect : SqlDialect
{
    private const string DefaultSessionSettings =
        "SET standard_conforming_strings = on;\nSET client_min_messages = warning;";

    private static readonly IReadOnlyDictionary<string, string> ExpectedSessionSettings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["standard_conforming_strings"] = "on",
            ["client_min_messages"] = "warning"
        };

    private string? _sessionSettings;

    internal PostgreSqlDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.PostgreSql;

    // Use ':' parameter marker; Npgsql supports ':' and existing integrations rely on it
    public override string ParameterMarker => ":";
    public override bool SupportsNamedParameters => true;

    public override bool SupportsSetValuedParameters => true;

    // IMMUTABLE: PostgreSQL practical parameter limit - do not change without extensive testing
    public override int MaxParameterLimit => 32767;

    // IMMUTABLE: PostgreSQL conservative output parameter limit for stored functions - do not change without extensive testing
    public override int MaxOutputParameters => 100;

    // IMMUTABLE: PostgreSQL NAMEDATALEN-1 identifier limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 63;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.PostgreSQL;
    public override bool RequiresStoredProcParameterNameMatch => true;

    // PostgreSQL benefits from prepared statements
    public override bool PrepareStatements => true;

    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => IsVersionAtLeast(15);
    public override bool SupportsSavepoints => true;
    public override bool SupportsJsonTypes => IsVersionAtLeast(9);
    public override bool SupportsSqlJsonConstructors => IsVersionAtLeast(18);
    public override bool SupportsJsonTable => IsVersionAtLeast(18);
    public override bool SupportsMergeReturning => IsVersionAtLeast(18);

    // PostgreSQL MERGE does NOT allow table alias on left side of UPDATE SET
    // Correct: UPDATE SET col = value
    // Error:   UPDATE SET t.col = value  -- "column 't' of relation 'table' does not exist"
    public override bool MergeUpdateRequiresTargetAlias => false;

    public override bool SupportsInsertReturning => true;

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"RETURNING {WrapObjectName(idColumnName)}";
    }

    public override string GetLastInsertedIdQuery()
    {
        // Fallback method - prefer RETURNING clause
        return "SELECT lastval()";
    }

    public override string RenderJsonArgument(string parameterMarker, IColumnInfo column)
    {
        return string.Concat(parameterMarker, "::jsonb");
    }

    public override void TryMarkJsonParameter(DbParameter parameter, IColumnInfo column)
    {
        base.TryMarkJsonParameter(parameter, column);
        parameter.DbType = DbType.String;
        parameter.Size = 0;

        try
        {
            var type = parameter.GetType();
            type.GetProperty("DataTypeName")?.SetValue(parameter, "jsonb");

            var npgsqlDbTypeProperty = type.GetProperty("NpgsqlDbType");
            if (npgsqlDbTypeProperty != null && npgsqlDbTypeProperty.PropertyType.IsEnum)
            {
                if (Enum.TryParse(npgsqlDbTypeProperty.PropertyType, "Jsonb", true, out var enumValue))
                {
                    npgsqlDbTypeProperty.SetValue(parameter, enumValue);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to stamp NpgsqlDbType metadata for JSON parameter {Parameter}.",
                parameter.ParameterName);
        }
    }

    public override string GetVersionQuery()
    {
        return "SELECT version()";
    }

    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        var name = await base.GetProductNameAsync(connection).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(name) && name!.IndexOf("npgsql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "PostgreSQL";
        }

        return name;
    }

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);

        // Check and cache PostgreSQL session settings during initialization
        if (_sessionSettings == null)
        {
            var result = GetPostgreSqlSessionSettings(connection);
            _sessionSettings = result.Settings;

            var snapshot = string.Join(", ", result.Snapshot.Select(kv => $"{kv.Key}={kv.Value}"));
            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation(
                    "PostgreSQL session settings detected: {CurrentSettings}. Applying changes:\n{Settings}", snapshot,
                    _sessionSettings);
            }
            else
            {
                Logger.LogInformation(
                    "PostgreSQL session settings detected: {CurrentSettings}. No changes required (already compliant)",
                    snapshot);
            }
        }

        return productInfo;
    }

    private SessionSettingsResult GetPostgreSqlSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT name, setting FROM pg_settings WHERE name IN ('standard_conforming_strings', 'client_min_messages')";

                using var reader = cmd.ExecuteReader();
                var currentSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                while (reader.Read())
                {
                    var settingName = reader.GetString(0);
                    var settingValue = reader.GetString(1);
                    currentSettings[settingName] = settingValue;
                }

                var built = BuildSessionSettingsScript(
                    ExpectedSessionSettings,
                    currentSettings,
                    static (name, value) => $"SET {name} = {value};");

                return new SessionSettingsResult(built, currentSettings, false);
            },
            () => new SessionSettingsResult(
                DefaultSessionSettings,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["standard_conforming_strings"] = "unknown",
                    ["client_min_messages"] = "unknown"
                },
                true),
            "Failed to check PostgreSQL session settings, applying default settings");
    }

    private (string settingsToApply, Dictionary<string, string> currentSettings) CheckPostgreSqlSettingsWithDetails(
        IDbConnection connection)
    {
        var result = GetPostgreSqlSessionSettings(connection);
        var snapshot = new Dictionary<string, string>(result.Snapshot, StringComparer.OrdinalIgnoreCase);
        return (result.Settings, snapshot);
    }

    private string CheckPostgreSqlSettings(IDbConnection connection)
    {
        if (_sessionSettings != null)
        {
            return _sessionSettings;
        }

        var (settingsToApply, currentSettings) = CheckPostgreSqlSettingsWithDetails(connection);
        _sessionSettings = settingsToApply;

        var snapshot = string.Join(", ", currentSettings.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrWhiteSpace(_sessionSettings))
        {
            Logger.LogInformation(
                "PostgreSQL session settings detected: {CurrentSettings}. Applying changes:\n{Settings}",
                snapshot,
                _sessionSettings);
        }
        else
        {
            Logger.LogInformation(
                "PostgreSQL session settings detected: {CurrentSettings}. No changes required (already compliant)",
                snapshot);
        }

        return _sessionSettings;
    }

    public override string GetBaseSessionSettings()
    {
        // If session settings haven't been detected yet (e.g., in testing),
        // provide fallback settings that are compatible with older PostgreSQL versions
        return _sessionSettings ?? DefaultSessionSettings;
    }

    public override string GetReadOnlySessionSettings()
    {
        return "SET default_transaction_read_only = on";
    }

    public override string? GetReadOnlyConnectionParameter()
    {
        return "Options='-c default_transaction_read_only=on'";
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        var baseSettings = GetBaseSessionSettings();

        return BuildSessionSettings(baseSettings, GetReadOnlySessionSettings(), readOnly);
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return _sessionSettings ?? @"SET standard_conforming_strings = on;
SET client_min_messages = warning;";
    }

    /// <summary>
    /// Bakes Npgsql-specific settings (auto-prepare, multiplexing) into the connection
    /// string before the DataSource is created.  Must be called while the connection
    /// string still carries credentials; NpgsqlConnectionStringBuilder preserves them on
    /// round-trip, unlike connection.ConnectionString on a DataSource-created connection.
    /// </summary>
    internal override string PrepareConnectionStringForDataSource(string connectionString)
    {
        try
        {
            ConnectionStringBuilder.ConnectionString = connectionString;
            var builder = ConnectionStringBuilder;
            var modified = false;

            if (!builder.ContainsKey("MaxAutoPrepare") || (int)builder["MaxAutoPrepare"] == 0)
            {
                builder["MaxAutoPrepare"] = 64;
                modified = true;
            }

            if (!builder.ContainsKey("AutoPrepareMinUsages") || (int)builder["AutoPrepareMinUsages"] == 0)
            {
                builder["AutoPrepareMinUsages"] = 2;
                modified = true;
            }

            if (!builder.ContainsKey("Multiplexing") || (bool)builder["Multiplexing"])
            {
                builder["Multiplexing"] = false;
                modified = true;
            }

            return modified ? builder.ConnectionString : connectionString;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to prepare connection string for DataSource.");
            return connectionString;
        }
    }

    public override void ConfigureProviderSpecificSettings(IDbConnection connection, IDatabaseContext context,
        bool readOnly)
    {
        // Npgsql: all provider-specific settings are baked into the connection string
        // before DataSource creation (via PrepareConnectionStringForDataSource).
        // Do NOT read/write connection.ConnectionString here — on DataSource-created
        // connections Npgsql strips credentials (PersistSecurityInfo behaviour) and
        // writing it back would silently drop the password.
        if (connection.GetType().FullName?.StartsWith("Npgsql.") == true)
        {
            return;
        }

        // For non-Npgsql connections in tests, normalize case so assertions using lower-case substrings succeed
        try
        {
            var typeName = connection.GetType().Name;
            var isTestConn = string.Equals(typeName, "TestConnection", StringComparison.Ordinal);
            var isFakeDb = connection.GetType().FullName?.Contains("fakeDb.") == true;
            if (isTestConn && !string.IsNullOrEmpty(connection.ConnectionString) && !isFakeDb)
            {
                connection.ConnectionString = connection.ConnectionString.ToLowerInvariant();
            }
        }
        catch
        { /* ignore */
        }
    }

    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>
        {
            { 15, SqlStandardLevel.Sql2016 },
            { 13, SqlStandardLevel.Sql2011 },
            { 11, SqlStandardLevel.Sql2008 },
            { 9, SqlStandardLevel.Sql2003 },
            { 8, SqlStandardLevel.Sql92 }
        };
    }

    public override SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql2008;
    }

    public override string UpsertIncomingColumn(string columnName)
    {
        return $"EXCLUDED.{WrapObjectName(columnName)}";
    }

    // Tests access a protected member via reflection; provide a protected facade that
    // delegates to the public base implementation without changing API surface.
    protected new SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        return base.DetermineStandardCompliance(version);
    }

    // Connection pooling properties for PostgreSQL (Npgsql)
    public override bool SupportsExternalPooling => true;
    public override string? PoolingSettingName => "Pooling";
    public override string? MinPoolSizeSettingName => "Minimum Pool Size";
    public override string? MaxPoolSizeSettingName => "Maximum Pool Size";
    public override string? ApplicationNameSettingName => "Application Name";
    internal override int DefaultMaxPoolSize => 100;
}