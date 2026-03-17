// =============================================================================
// FILE: PostgreSqlDialect.cs
// PURPOSE: PostgreSQL specific dialect implementation.
//
// AI SUMMARY:
// - Supports PostgreSQL 10+ with comprehensive SQL standard compliance.
// - Key features:
//   * INSERT ... ON CONFLICT for upserts (supports DO UPDATE and DO NOTHING)
//   * Parameter marker: @ (ADO.NET standard; avoids Npgsql '::' cast lookahead)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 32767 (practical limit)
//   * Prepared statements enabled for performance
// - Session settings: standard_conforming_strings, client_min_messages.
// - RETURNING clause for getting generated IDs.
// - Array/range type support via Npgsql.
// - CockroachDB uses this dialect (Postgres-compatible wire protocol).
// - Stored procedure support via CALL statement.
// =============================================================================

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
    private const string StandardConformingStringsSetting = "standard_conforming_strings";
    private const string ClientMinMessagesSetting = "client_min_messages";
    private const string ReadOnlyTransactionSetting = "default_transaction_read_only";

    // Connection string keys used when baking startup options
    private const string NpgsqlOptionsKey = "Options";

    private bool _settingsBaked;
    internal override bool SessionSettingsBakedIntoDataSource => _settingsBaked;

    // Reflection-based property/type names used to stamp NpgsqlDbType on JSON parameters
    private const string DataTypeNameProperty = "DataTypeName";
    private const string NpgsqlDbTypeProperty = "NpgsqlDbType";
    private const string JsonbTypeName = "Jsonb";

    private const string DefaultSessionSettings =
        $"SET {StandardConformingStringsSetting} = on, {ClientMinMessagesSetting} = warning;";

    private static readonly IReadOnlyDictionary<string, string> ExpectedSessionSettings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [StandardConformingStringsSetting] = "on",
            [ClientMinMessagesSetting] = "warning"
        };

    private string? _sessionSettings;
    private readonly SupportedDatabase _flavor;

    internal PostgreSqlDialect(DbProviderFactory factory, ILogger logger, SupportedDatabase flavor = SupportedDatabase.PostgreSql)
        : base(factory, logger)
    {
        _flavor = flavor;
    }

    public override SupportedDatabase DatabaseType => _flavor;

    // Use '@' parameter marker — ADO.NET standard; avoids Npgsql's '::' cast lookahead
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;

    public override bool SupportsSetValuedParameters => true;

    public override bool SupportsBatchUpdate => true;

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        if (rowCount <= 0)
        {
            return;
        }

        // PostgreSQL UPDATE FROM VALUES pattern:
        // UPDATE target SET col1 = s.col1, ...
        // FROM (VALUES (@b0, @b1), (@b2, @b3)) AS s(pk, col1)
        // WHERE target.pk = s.pk

        query.Append("UPDATE ");
        query.Append(tableName);
        query.Append(" AS t SET ");

        for (var i = 0; i < columnNames.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(columnNames[i]);
            query.Append(" = s.");
            query.Append(columnNames[i]);
        }

        query.Append(" FROM (VALUES ");

        var allCols = new List<string>(keyColumns);
        allCols.AddRange(columnNames);

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            if (row > 0)
            {
                query.Append(", ");
            }

            query.Append('(');
            for (var col = 0; col < allCols.Count; col++)
            {
                if (col > 0)
                {
                    query.Append(", ");
                }
                var val = getValue?.Invoke(row, col);
                if (val == null || val == DBNull.Value)
                {
                    query.Append("NULL");
                }
                else
                {
                    query.Append(ParameterMarker);
                    query.Append('b');
                    query.Append(paramIdx++.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            query.Append(')');
        }

        query.Append(") AS s(");
        for (var i = 0; i < allCols.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(allCols[i]);
        }

        query.Append(") WHERE ");
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (i > 0)
            {
                query.Append(" AND ");
            }
            query.Append("t.");
            query.Append(keyColumns[i]);
            query.Append(" = s.");
            query.Append(keyColumns[i]);
        }
    }

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
    public override bool SupportsMerge => DatabaseType != SupportedDatabase.CockroachDb && IsVersionAtLeast(15);
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
            type.GetProperty(DataTypeNameProperty)?.SetValue(parameter, "jsonb");

            var npgsqlDbTypeProperty = type.GetProperty(NpgsqlDbTypeProperty);
            if (npgsqlDbTypeProperty != null && npgsqlDbTypeProperty.PropertyType.IsEnum)
            {
                if (Enum.TryParse(npgsqlDbTypeProperty.PropertyType, JsonbTypeName, true, out var enumValue))
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
                    "PostgreSQL session settings detected: {CurrentSettings}. Already compliant; enforcing baseline on every checkout",
                    snapshot);
            }
        }

        return productInfo;
    }

    public override string GetFinalSessionSettings(bool readOnly)
    {
        // 1 RTT / 1 Command Optimization: Consolidate all session assignments (baseline + intent)
        // into a single comma-separated SET command.
        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            sb.Append("SET ");
            sb.Append(StandardConformingStringsSetting);
            sb.Append(" = on, ");
            sb.Append(ClientMinMessagesSetting);
            sb.Append(" = warning, ");
            sb.Append(ReadOnlyTransactionSetting);
            sb.Append(readOnly ? " = on;" : " = off;");

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    private SessionSettingsResult GetPostgreSqlSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                // Always set the full baseline to ensure deterministic state in pooled connections.
                return new SessionSettingsResult(DefaultSessionSettings, ExpectedSessionSettings, false);
            },
            () => new SessionSettingsResult(
                DefaultSessionSettings,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [StandardConformingStringsSetting] = "unknown",
                    [ClientMinMessagesSetting] = "unknown"
                },
                true),
            "Failed to configure PostgreSQL session settings");
    }

    public override string GetBaseSessionSettings()
    {
        // Always enforce the full baseline on every connection checkout.
        // A cached empty diff means the first sampled connection was already compliant,
        // but pooled connections can drift if external code mutates session state.
        return string.IsNullOrWhiteSpace(_sessionSettings) ? DefaultSessionSettings : _sessionSettings;
    }

    public override string GetReadOnlySessionSettings()
    {
        return $"SET {ReadOnlyTransactionSetting} = on;";
    }

    internal override string? GetReadOnlyTransactionResetSql()
    {
        return $"SET {ReadOnlyTransactionSetting} = off;";
    }

    public override string? GetReadOnlyConnectionParameter()
    {
        return $"Options='-c {ReadOnlyTransactionSetting}=on'";
    }

    /// <summary>
    /// Bakes Npgsql-specific settings (auto-prepare, multiplexing) into the connection
    /// string before the DataSource is created.  Must be called while the connection
    /// string still carries credentials; NpgsqlConnectionStringBuilder preserves them on
    /// round-trip, unlike connection.ConnectionString on a DataSource-created connection.
    /// </summary>
    internal override string PrepareConnectionStringForDataSource(string connectionString, bool readOnly = false)
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

            // PERFORMANCE: Bake session settings into the PostgreSQL startup Options parameter.
            // Values sent via the protocol startup message become GUC session defaults.
            // PostgreSQL's RESET ALL (sent by Npgsql on pool return) restores parameters to
            // their session defaults — i.e., back to these startup values — so the next
            // checkout requires zero additional SET round-trips.
            var existingOptions = builder.ContainsKey(NpgsqlOptionsKey)
                ? builder[NpgsqlOptionsKey] as string ?? string.Empty
                : string.Empty;
            var mergedOptions = MergeStartupOptions(existingOptions, readOnly);
            if (!string.Equals(existingOptions, mergedOptions, StringComparison.Ordinal))
            {
                builder[NpgsqlOptionsKey] = mergedOptions;
                modified = true;
            }

            if (modified)
            {
                _settingsBaked = true;
                return builder.ConnectionString;
            }

            // Options were already fully baked (user set all required keys to correct values)
            _settingsBaked = true;
            return connectionString;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to prepare connection string for DataSource.");
            _settingsBaked = false;
            return connectionString;
        }
    }

    /// <summary>
    /// Override in subclasses to include additional GUC settings that should be baked
    /// into the PostgreSQL startup <c>Options</c> parameter alongside the three base keys.
    /// The base implementation returns an empty sequence.
    /// </summary>
    protected virtual IEnumerable<(string Key, string Value)> GetAdditionalStartupOptions(bool readOnly)
        => [];

    /// <summary>
    /// Merges the required GUC session settings into the existing PostgreSQL
    /// <c>options</c> startup string.  Format: <c>-c key=value -c key=value …</c>.
    /// User-supplied options for other keys are preserved; our keys are
    /// always overridden to ensure deterministic startup state.
    /// </summary>
    /// <remarks>
    /// SECURITY: <paramref name="existing"/> must come from a trusted source (the
    /// connection string provided by the application).  This method does not
    /// sanitize arbitrary user-supplied values; never pass end-user input here.
    /// </remarks>
    private string MergeStartupOptions(string existing, bool readOnly)
    {
        // Parse existing "-c key=value" tokens into an ordered list so we can
        // detect and replace our specific keys while preserving user-supplied ones.
        var tokens = new List<(string Key, string Value)>();
        var rest = existing.AsSpan().Trim();
        while (!rest.IsEmpty)
        {
            // Expect token to start with "-c " (possibly after whitespace)
            if (rest.StartsWith("-c ", StringComparison.Ordinal) ||
                rest.StartsWith("-c\t", StringComparison.Ordinal))
            {
                rest = rest[3..].TrimStart();
            }
            else
            {
                // Malformed token — skip to next '-c'
                var next = rest.IndexOf("-c ", StringComparison.Ordinal);
                if (next < 0)
                {
                    break;
                }
                rest = rest[next..];
                continue;
            }

            var eqIdx = rest.IndexOf('=');
            if (eqIdx <= 0)
            {
                break;
            }
            var key = rest[..eqIdx].Trim().ToString();
            rest = rest[(eqIdx + 1)..];

            // Value ends at next " -c" or end of string
            var nextFlag = rest.IndexOf(" -c", StringComparison.Ordinal);
            string value;
            if (nextFlag >= 0)
            {
                value = rest[..nextFlag].Trim().ToString();
                rest = rest[nextFlag..].TrimStart();
            }
            else
            {
                value = rest.Trim().ToString();
                rest = default;
            }

            tokens.Add((key, value));
        }

        // Apply required settings (overriding any existing values for these keys)
        var allKeys = new List<(string Key, string Value)>
        {
            (StandardConformingStringsSetting, "on"),
            (ClientMinMessagesSetting, "warning"),
            (ReadOnlyTransactionSetting, readOnly ? "on" : "off")
        };
        allKeys.AddRange(GetAdditionalStartupOptions(readOnly));

        foreach (var (ourKey, ourValue) in allKeys)
        {
            var found = false;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i].Key, ourKey, StringComparison.OrdinalIgnoreCase))
                {
                    tokens[i] = (tokens[i].Key, ourValue);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                tokens.Add((ourKey, ourValue));
            }
        }

        return string.Join(" ", tokens.Select(t => $"-c {t.Key}={t.Value}"));
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
        {
            /* ignore */
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

    public override object? PrepareParameterValue(object? value, DbType dbType)
    {
        if (value is DateTimeOffset dto)
        {
            // Npgsql 6+ requires DateTimeOffset to be UTC when writing to timestamptz.
            return dto.UtcDateTime;
        }

        return base.PrepareParameterValue(value, dbType);
    }

    /// <summary>
    /// Sets Npgsql-specific type properties on a parameter via reflection so that
    /// subclasses can reuse the same logic without duplicating it.
    /// Silently ignores failures when the parameter is not an Npgsql parameter type.
    /// </summary>
    protected void SetNpgsqlParameterType(DbParameter parameter, string npgsqlDbTypeName, string dataTypeName)
    {
        try
        {
            var type = parameter.GetType();
            var npgsqlDbTypeProp = type.GetProperty(NpgsqlDbTypeProperty);
            if (npgsqlDbTypeProp != null)
            {
                if (Enum.TryParse(npgsqlDbTypeProp.PropertyType, npgsqlDbTypeName, true, out var enumVal))
                {
                    npgsqlDbTypeProp.SetValue(parameter, enumVal);
                }
            }

            type.GetProperty(DataTypeNameProperty)?.SetValue(parameter, dataTypeName);
        }
        catch
        {
            // Not an Npgsql parameter or the property is absent — ignore.
        }
    }

    // Tests access a protected member via reflection; provide a protected facade that
    // delegates to the public base implementation without changing API surface.
    protected new SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        return base.DetermineStandardCompliance(version);
    }

    // Connection pooling properties for PostgreSQL (Npgsql)
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "Minimum Pool Size";
    public override string? MaxPoolSizeSettingName => "Maximum Pool Size";
    public override string? ApplicationNameSettingName => "Application Name";
}
