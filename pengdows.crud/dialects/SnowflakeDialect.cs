// =============================================================================
// FILE: SnowflakeDialect.cs
// PURPOSE: Snowflake-specific SQL dialect implementation.
//
// AI SUMMARY:
// - Supports Snowflake cloud data warehouse with SQL:2016 compliance.
// - Key features:
//   * Parameter marker: : (colon prefix, Snowflake.Data standard)
//   * Identifier quoting: "name" (double-quotes; unquoted identifiers fold to UPPERCASE)
//   * MERGE statement for upserts (uses src.{col} alias pattern)
//   * LAST_INSERT_ID() fallback for generated key retrieval (no RETURNING support)
//   * Savepoints NOT supported
//   * No Docker image available; uses credential-based external connection
// - Uses plain DbType mappings (Snowflake.Data driver, not Npgsql)
// - PrepareStatements enabled for performance
// - SupportsNamespaces: true (db.schema.table fully qualified)
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Snowflake dialect with SQL:2016 compliance and MERGE-based upsert.
/// </summary>
/// <remarks>
/// <para>
/// Supports the Snowflake cloud data warehouse via the official Snowflake.Data .NET connector.
/// Uses colon-prefixed named parameters and double-quote identifier quoting.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement with <c>src.{col}</c> alias for incoming values.
/// </para>
/// <para>
/// <strong>Prepared Statements:</strong> Enabled by default for performance.
/// </para>
/// </remarks>
internal class SnowflakeDialect : SqlDialect
{
    private const string CanonicalSessionSettings =
        "ALTER SESSION SET TIMEZONE = 'UTC', TIMESTAMP_OUTPUT_FORMAT = 'YYYY-MM-DD HH24:MI:SS.FF3', CLIENT_TIMESTAMP_TYPE_MAPPING = TIMESTAMP_NTZ, LOCK_TIMEOUT = 30000;";

    internal SnowflakeDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Snowflake;

    // Snowflake.Data uses colon-prefixed named parameters
    public override string ParameterMarker => ":";
    public override bool SupportsNamedParameters => true;

    // Double-quote identifiers; unquoted Snowflake identifiers fold to UPPERCASE
    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";

    // Snowflake has strong SQL:2016 compliance
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : SqlStandardLevel.Sql2016;

    public override bool PrepareStatements => true;

    // Snowflake stored procedures use CALL proc_name(args) syntax.
    // ProcWrappingStyle.None would map to UnsupportedProcWrappingStrategy and throw at runtime.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;

    // Snowflake stored procedures support many parameters; the base default of 0 is incorrect.
    public override int MaxOutputParameters => 65535;

    // Snowflake stores GUIDs as VARCHAR(36) — handled here via GuidFormat rather than
    // AdvancedTypeRegistry so the mapping is explicit, testable, and dialect-co-located.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    public override bool SupportsNamespaces => true;

    // Snowflake optimized batching
    public override int MaxRowsPerBatch => 16384; // Optimized for cloud warehouse bulk loads
    public override int MaxParameterLimit => 65535; // Snowflake driver limit is high

    public override bool SupportsBatchUpdate => true;

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        if (rowCount <= 0) return;

        // Snowflake UPDATE FROM VALUES pattern:
        // UPDATE target SET col1 = s.col1, ...
        // FROM (VALUES (:b0, :b1), (:b2, :b3)) AS s(pk, col1)
        // WHERE target.pk = s.pk

        query.Append("UPDATE ");
        query.Append(tableName);
        query.Append(" SET ");

        for (var i = 0; i < columnNames.Count; i++)
        {
            if (i > 0) query.Append(", ");
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
            if (row > 0) query.Append(", ");
            query.Append('(');
            for (var col = 0; col < allCols.Count; col++)
            {
                if (col > 0) query.Append(", ");
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
            if (i > 0) query.Append(", ");
            query.Append(allCols[i]);
        }

        query.Append(") WHERE ");
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (i > 0) query.Append(" AND ");
            query.Append(tableName);
            query.Append('.');
            query.Append(keyColumns[i]);
            query.Append(" = s.");
            query.Append(keyColumns[i]);
        }
    }

    // MERGE-based upsert (no ON CONFLICT)
    public override bool SupportsMerge => true;
    public override bool SupportsMergeReturning => false;
    public override bool SupportsInsertOnConflict => false;

    // Snowflake does NOT support INSERT...RETURNING or LAST_INSERT_ID().
    // Use client-generated IDs ([Id(true)] with UUID/Snowflake IDs) for reliable key capture.
    public override bool SupportsInsertReturning => false;

    public override bool SupportsSavepoints => false;

    public override bool SupportsDropTableIfExists => true;

    // Snowflake MERGE: target alias is allowed on UPDATE SET columns
    public override bool MergeUpdateRequiresTargetAlias => true;

    public override string? UpsertIncomingAlias => "src";

    public override string UpsertIncomingColumn(string columnName)
    {
        return $"src.{WrapObjectName(columnName)}";
    }

    public override string GetVersionQuery()
    {
        return "SELECT CURRENT_VERSION()";
    }

    public override System.Version? ParseVersion(string versionString)
    {
        // Snowflake reports versions like "7.43.0" or "8.12.1"
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        if (System.Version.TryParse(versionString.Trim(), out var v))
        {
            return v;
        }

        return base.ParseVersion(versionString);
    }

    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>
        {
            { 8, SqlStandardLevel.Sql2019 },
            { 7, SqlStandardLevel.Sql2016 }
        };
    }

    public override SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql2016;
    }

    public override object? PrepareParameterValue(object? value, DbType dbType)
    {
        if (value is DateTimeOffset dto)
        {
            // Snowflake TIMESTAMP_NTZ does not store offsets; normalize to UTC instant.
            return dto.UtcDateTime;
        }

        return base.PrepareParameterValue(value, dbType);
    }

    /// <summary>
    /// Pass through — Snowflake.Data handles warehouse/role/schema in the connection string.
    /// </summary>
    internal override string PrepareConnectionStringForDataSource(string connectionString, bool readOnly = false)
    {
        return connectionString;
    }

    private string? _sessionSettings;

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);

        if (_sessionSettings == null)
        {
            var result = GetSnowflakeSessionSettings(connection);
            _sessionSettings = result.Settings;

            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying Snowflake session settings on first connect:\n{Settings}",
                    _sessionSettings);
            }
        }

        return productInfo;
    }

    public override string GetFinalSessionSettings(bool readOnly)
    {
        // 1 RTT / 1 Command Optimization: Consolidate all session assignments into a single
        // comma-separated ALTER SESSION SET command.
        //
        // CLIENT_TIMESTAMP_TYPE_MAPPING = TIMESTAMP_NTZ: the Snowflake.Data driver defaults
        // to TIMESTAMP_LTZ for DateTime bind variables; since the dialect normalises
        // DateTimeOffset → UTC DateTime for NTZ columns we must override this to prevent
        // the driver from attaching timezone metadata at bind time.
        //
        // LOCK_TIMEOUT = 30000 s (≈8.3 h): intentionally shorter than the Snowflake default
        // of 43200 s (12 h); long-held locks in a data-access layer indicate a bug.
        //
        // Note: Snowflake does not have a session-level read-only mode. TRANSACTION_READ_ONLY
        // is not a valid ALTER SESSION SET parameter. Read-only intent must be enforced through
        // Snowflake role/warehouse permissions or read-only credentials, not session SQL.
        return CanonicalSessionSettings;
    }

    private SessionSettingsResult GetSnowflakeSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                // Snowflake ALTER SESSION SET allows comma-separated assignments in a single command.
                // This is the optimal "Always SET" pattern for Snowflake.
                var script = CanonicalSessionSettings;

                return new SessionSettingsResult(script, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TIMEZONE"] = "UTC",
                    ["TIMESTAMP_OUTPUT_FORMAT"] = "YYYY-MM-DD HH24:MI:SS.FF3",
                    ["CLIENT_TIMESTAMP_TYPE_MAPPING"] = "TIMESTAMP_NTZ",
                    ["LOCK_TIMEOUT"] = "30000"
                }, false);
            },
            () => new SessionSettingsResult(
                CanonicalSessionSettings,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                true),
            "Failed to configure Snowflake session settings");
    }

    public override string GetBaseSessionSettings()
    {
        return CanonicalSessionSettings;
    }

    // Connection pooling properties for Snowflake
    public override string? MinPoolSizeSettingName => "minPoolSize";
    public override string? MaxPoolSizeSettingName => "maxPoolSize";
    public override string? ApplicationNameSettingName => "application";
}
