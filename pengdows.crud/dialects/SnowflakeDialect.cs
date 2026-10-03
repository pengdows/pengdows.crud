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
//   * No RETURNING or LAST_INSERT_ID(); generated keys use the base correlation-token fallback
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
        "ALTER SESSION SET TIMEZONE = 'UTC', TIMESTAMP_OUTPUT_FORMAT = 'YYYY-MM-DD HH24:MI:SS.FF3', CLIENT_TIMESTAMP_TYPE_MAPPING = TIMESTAMP_NTZ, GEOGRAPHY_OUTPUT_FORMAT = 'EWKT', GEOMETRY_OUTPUT_FORMAT = 'EWKT', LOCK_TIMEOUT = 30000;";

    internal SnowflakeDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    // Spatial values bind as EWKT text, so a NULL spatial value binds as text too (WRT-011:
    // Snowflake.Data threw "No corresponding Snowflake type for type Object").
    internal override DbType? NullParameterDbType(IColumnInfo column)
    {
        var type = column.PropertyInfo.PropertyType;
        return typeof(types.valueobjects.SpatialValue).IsAssignableFrom(type)
            ? DbType.String
            : base.NullParameterDbType(column);
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Snowflake;

    // Snowflake.Data uses colon-prefixed named parameters
    public override string ParameterMarker => ":";
    public override bool SupportsNamedParameters => true;

    // Double-quote identifiers; unquoted Snowflake identifiers fold to UPPERCASE
    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";

    // Snowflake has strong SQL:2016 compliance
    [Obsolete("MaxSupportedStandard is a coarse heuristic; query the specific Supports* capability instead.", false)]
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : SqlStandardLevel.Sql2016;

    public override bool PrepareStatements => true;

    // Snowflake parses constraint DDL but does not enforce any constraints at runtime.
    public override bool EnforcesConstraints => false;
    public override bool EnforcesForeignKeyConstraints => false;
    public override bool SupportsUniqueConstraints => false;
    public override bool SupportsCheckConstraints => false;

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

    // VARIANT/OBJECT/ARRAY: Snowflake.Data can't bind them ("Snowflake type VARIANT is not supported
    // for parameters"), so a JSON value is bound as text and parsed server-side. Snowflake refuses
    // PARSE_JSON(:p) inside INSERT ... VALUES but takes it in a SELECT, so writes carrying one take
    // their values from a SELECT (all confirmed live, TYPE-002).
    public override string RenderJsonArgument(string parameterMarker, IColumnInfo column) =>
        string.Concat("PARSE_JSON(", parameterMarker, ")");

    internal override bool AllowsColumnArgumentsInValues => false;

    // Snowflake.Data reports TIMESTAMP_LTZ/TZ as DateTime; GetValue returns the DateTimeOffset.
    internal override bool ReportsOffsetTimestampsAsDateTime => true;

    /// <summary>
    /// "USING (SELECT :p AS col, ...) AS s": the base "USING (VALUES (...))" source can't carry
    /// PARSE_JSON(:p); a SELECT can, for every column.
    /// </summary>
    public override string RenderMergeSource(IReadOnlyList<IColumnInfo> columns, IReadOnlyList<string> parameterNames)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(parameterNames);
        if (columns.Count != parameterNames.Count)
        {
            throw new ArgumentException("Column and parameter counts must match.");
        }

        var select = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                select.Append(", ");
            }

            select.Append(RenderColumnArgument(MakeParameterName(parameterNames[i]), columns[i]));
            select.Append(" AS ");
            select.Append(WrapObjectName(columns[i].Name));
        }

        return string.Concat("USING (SELECT ", select.ToString(), ") AS s");
    }

    internal override void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
        ISqlQueryBuilder query, Func<int, int, object?>? getValue, IReadOnlyList<IColumnInfo> columns)
    {
        AppendAnsiBatchInsert(tableName, columnNames, rowCount, query, getValue, columns);
    }

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        AppendBatchUpdate(tableName, columnNames, keyColumns, rowCount, query, getValue, null);
    }

    internal override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo> columns)
    {
        AppendBatchUpdate(tableName, columnNames, keyColumns, rowCount, query, getValue, columns);
    }

    private void AppendBatchUpdate(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo>? columns)
    {
        if (rowCount <= 0)
        {
            return;
        }

        // Snowflake UPDATE FROM VALUES pattern:
        // UPDATE target SET col1 = s.col1, ...
        // FROM (VALUES (:b0, :b1), (:b2, :b3)) AS s(pk, col1)
        // WHERE target.pk = s.pk

        query.Append("UPDATE ");
        query.Append(tableName);
        query.Append(" SET ");

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

        var allCols = new List<string>(keyColumns);
        allCols.AddRange(columnNames);

        // FROM (SELECT :b0 AS pk, PARSE_JSON(:b1) AS col UNION ALL SELECT ...) AS s when a column
        // renders an argument, which VALUES can't hold.
        var fromSelect = InsertsFromSelect(columns);
        query.Append(fromSelect ? " FROM (" : " FROM (VALUES ");

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            if (fromSelect)
            {
                query.Append(row > 0 ? " UNION ALL SELECT " : "SELECT ");
            }
            else
            {
                if (row > 0)
                {
                    query.Append(", ");
                }

                query.Append('(');
            }

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
                    AppendBatchValue(query, columns, col, paramIdx++);
                }

                if (fromSelect)
                {
                    query.Append(" AS ");
                    query.Append(allCols[col]);
                }
            }

            if (!fromSelect)
            {
                query.Append(')');
            }
        }

        if (fromSelect)
        {
            query.Append(") AS s WHERE ");
        }
        else
        {
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
        }
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (i > 0)
            {
                query.Append(" AND ");
            }

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

    // No RETURNING, no sequence the gateway prefetches, no last-id function: a [CorrelationToken]
    // column is the only way to read a database-generated id back (DEC-007).
    internal override bool RequiresCorrelationTokenForGeneratedIds => true;

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

        // Snowflake.Data binds DbType.Time only from a DateTime and sends its time of day
        // (SFDataConverter.CSharpValToSfVal); a TimeSpan fails with error 270003. A value outside
        // one day is passed through so the driver rejects it rather than silently wrapping it.
        if (dbType == DbType.Time && value is TimeSpan ts && ts >= TimeSpan.Zero && ts.Ticks < TimeSpan.TicksPerDay)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).Add(ts);
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

    // Snowflake.Data 4.x defaults maxPoolSize to 10. Keep pengdows.crud aligned
    // with the provider unless the caller explicitly configures a different limit.
    internal override int DefaultMaxPoolSize => 10;

    // Snowflake parses UNIQUE/PRIMARY KEY constraint DDL but never enforces it at runtime
    // (SupportsUniqueConstraints = false) — this exception category structurally cannot occur, so
    // explicit false instead of falling through to the generic message-based default.
    public override bool IsUniqueViolation(DbException ex) => false;

    // Snowflake parses FOREIGN KEY constraint DDL but never enforces it at runtime
    // (EnforcesForeignKeyConstraints = false) — this exception category structurally cannot occur.
    public override bool IsForeignKeyViolation(DbException ex) => false;

    // NOT NULL is the one constraint Snowflake actually enforces at runtime (error 100072,
    // SQLSTATE 23502). Message wording is "NULL result in a non-nullable column" — the generic
    // default's "not null"/"not-null" check does not match "non-nullable", so this needs its own
    // override.
    public override bool IsNotNullViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23502", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("non-nullable", StringComparison.OrdinalIgnoreCase);

    // Snowflake parses CHECK constraint DDL but never enforces it at runtime
    // (SupportsCheckConstraints = false) — this exception category structurally cannot occur.
    public override bool IsCheckConstraintViolation(DbException ex) => false;
}
