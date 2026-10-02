// =============================================================================
// FILE: SqlServerDialect.cs
// PURPOSE: SQL Server specific dialect implementation.
//
// AI SUMMARY:
// - Supports SQL Server 2012+ with version-specific feature detection.
// - Key features:
//   * MERGE statement support for upserts
//   * Parameter marker: @ (supports named parameters)
//   * Identifier quoting: "name" (ANSI double-quotes, NOT brackets)
//     QUOTED_IDENTIFIER is ON (driver login default at modern compatibility
//     levels, otherwise SET via session settings); the base-class
//     default of " is intentionally kept.  Do not add QuotePrefix/QuoteSuffix
//     overrides here.
//   * Max parameters: 2100 (sp_executesql limit)
//   * Session settings: ANSI_NULLS, QUOTED_IDENTIFIER, etc.
// - Session settings enforced for consistent behavior across connections.
// - Snapshot isolation detection via sys.databases queries.
// - OFFSET/FETCH pagination (SQL Server 2012+).
// - IDENTITY column handling with OUTPUT clause for returning IDs.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// SQL Server dialect with version-specific feature support.
/// </summary>
/// <remarks>
/// <para>
/// Supports Microsoft SQL Server 2012 and later with automatic version detection.
/// </para>
/// <para>
/// <strong>Session Settings:</strong> Enforces ANSI-compliant settings including
/// ANSI_NULLS, QUOTED_IDENTIFIER, and ARITHABORT for consistent behavior.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement with OUTPUT clause.
/// </para>
/// </remarks>
internal class SqlServerDialect : SqlDialect
{
    private const string RcsiQuery =
        "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()";

    private const string SnapshotIsolationQuery =
        "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()";

    // Single source of truth for session settings; both the SET script and the
    // expected-state dictionary are derived from this array.
    private static readonly (string Name, string Value)[] SessionSettingsDef =
    {
        ("ANSI_NULLS", "ON"),
        ("ANSI_PADDING", "ON"),
        ("ANSI_WARNINGS", "ON"),
        ("ARITHABORT", "ON"),
        ("CONCAT_NULL_YIELDS_NULL", "ON"),
        ("QUOTED_IDENTIFIER", "ON"),
        ("NUMERIC_ROUNDABORT", "OFF"),
    };

    private static readonly string DefaultSessionSettings =
        BuildDefaultSessionSettings();

    private static readonly IReadOnlyDictionary<string, string> ExpectedSessionSettings =
        BuildExpectedSessionSettings();

    private static string BuildDefaultSessionSettings()
    {
        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        for (var i = 0; i < SessionSettingsDef.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(";\n");
            }
            sb.Append("SET ");
            sb.Append(SessionSettingsDef[i].Name);
            sb.Append(' ');
            sb.Append(SessionSettingsDef[i].Value);
        }
        sb.Append(';');
        var result = sb.ToString();
        sb.Dispose();
        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildExpectedSessionSettings()
    {
        var dict = new Dictionary<string, string>(SessionSettingsDef.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var s in SessionSettingsDef)
        {
            dict[s.Name] = s.Value;
        }
        return dict;
    }

    private string? _sessionSettings;

    internal SqlServerDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;

    /// <inheritdoc />
    /// <remarks>
    /// LocalDB auto-shuts down after an idle period, so <see cref="DbMode.Best"/> selects
    /// PreventDatabaseUnload. That is a default, not a mandate: a workload busy enough never to go
    /// idle doesn't need the sentinel, so an explicit <see cref="DbMode.Standard"/> is honored
    /// (with a performance-only warning from DatabaseContext). The single-connection modes have no
    /// purpose on LocalDB and still resolve to PreventDatabaseUnload. Non-LocalDB SQL Server is an
    /// ordinary full server database and falls through to the base implementation.
    /// (Ported from 3.0 b356af1.)
    /// </remarks>
    public override (DbMode Mode, string Reason) CoerceConnectionMode(DbMode requested, string? connectionString,
        bool isLocalDb)
    {
        if (isLocalDb)
        {
            if (requested == DbMode.Standard)
            {
                return (DbMode.Standard, string.Empty);
            }

            return requested == DbMode.Best
                ? (DbMode.PreventDatabaseUnload, "LocalDB: Best selects PreventDatabaseUnload")
                : (DbMode.PreventDatabaseUnload, "LocalDB requires PreventDatabaseUnload");
        }

        return base.CoerceConnectionMode(requested, connectionString, isLocalDb);
    }

    // TYPE-005: SqlClient reads hierarchyid/geometry/geography through Microsoft.SqlServer.Types;
    // without that assembly GetValue throws FileNotFoundException (confirmed live).
    internal override bool IsUnreadableStoredValue(Exception exception) =>
        (exception switch
        {
            System.IO.FileNotFoundException missing => missing.FileName,
            System.IO.FileLoadException unloadable => unloadable.FileName,
            _ => null
        })?.StartsWith("Microsoft.SqlServer.Types", StringComparison.OrdinalIgnoreCase) == true;

    // TYPE-016: without Microsoft.SqlServer.Types, SqlClient reports no field type for hierarchyid
    // (GetDataTypeName "master.sys.hierarchyid") but GetBytes returns its stored encoding, which
    // UnresolvedColumnReader decodes as a HierarchyId (confirmed live on SQL Server 2025, SqlClient
    // 6.0.2). geometry/geography read the same way and SqlServerSpatialFormat decodes them (TYPE-002).
    // TYPE-015: VECTOR binds from "[...]" text; SqlClient rejects a float[] parameter.
    internal override bool BindsVectorsAsText => true;

    internal override bool ReadsUnresolvedColumns => true;

    internal override Type? GetUnresolvedColumnType(string dataTypeName) =>
        IsUdt(dataTypeName, "hierarchyid") ? typeof(HierarchyId)
        : IsUdt(dataTypeName, "geometry") ? typeof(Geometry)
        : IsUdt(dataTypeName, "geography") ? typeof(Geography)
        : null;

    private static bool IsUdt(string dataTypeName, string name) =>
        dataTypeName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
        (dataTypeName.EndsWith(name, StringComparison.OrdinalIgnoreCase) &&
         dataTypeName.Length > name.Length && dataTypeName[dataTypeName.Length - name.Length - 1] == '.');

    // SQL Server uses OFFSET/FETCH NEXT syntax only — no LIMIT keyword.
    public override bool SupportsLimitOffset => false;
    public override string ParameterMarker => "@";

    public override void AppendPaging(ISqlQueryBuilder query, int offset, int limit)
    {
        var sql = query.ToString();
        if ((sql.Length > 0) && !sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SQL Server OFFSET/FETCH paging requires ORDER BY.");
        }

        base.AppendPaging(query, offset, limit);
    }

    // DO NOT override QuotePrefix / QuoteSuffix here.
    // QUOTED_IDENTIFIER is ON for every connection (driver login default at modern
    // compatibility levels; otherwise SET via SessionSettingsDef), so identifiers are
    // quoted with ANSI double-quotes ("name").
    // The base-class defaults (" / ") are exactly what we want.
    // SQL Server also accepts [...] brackets, but this codebase deliberately
    // uses the ANSI style for consistency across all dialects.

    public override bool SupportsNamedParameters => true;

    // IMMUTABLE: SQL Server sp_executesql documented limit - do not change without extensive testing
    public override int MaxParameterLimit => 2100;

    // IMMUTABLE: SQL Server output parameter limit - do not change without extensive testing  
    public override int MaxOutputParameters => 1024;

    // IMMUTABLE: SQL Server identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 128;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Exec;

    // SQL Server relies on sp_executesql and server plan cache, not manual prepare
    public override bool PrepareStatements => false;
    public override bool SupportsReadOnlyTransactions => true;

    [Obsolete("MaxSupportedStandard is a coarse heuristic; query the specific Supports* capability instead.", false)]
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    // IMMUTABLE: SQL Server VALUES clause limit - do not change without extensive testing
    public override int MaxRowsPerBatch => 1000;

    public override bool SupportsBatchUpdate => true;

    // TYPE-003, confirmed live 2026-09-30: the provider rejects DbType.SByte and the unsigned DbTypes.
    internal override bool BindsSByteAndUnsignedNatively => false;

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        AppendBatchMerge(tableName, columnNames, keyColumns, rowCount, query, getValue, null);
    }

    internal override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo> columns)
    {
        AppendBatchMerge(tableName, columnNames, keyColumns, rowCount, query, getValue, columns);
    }

    internal override void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
        ISqlQueryBuilder query, Func<int, int, object?>? getValue, IReadOnlyList<IColumnInfo> columns)
    {
        AppendAnsiBatchInsert(tableName, columnNames, rowCount, query, getValue, columns);
    }

    // TYPE-002: geometry/geography are built by the server from a big-endian SRID + WKB
    // (SqlServerSpatialFormat.ToConstructorArgument), so SQL Server validates the instance and keeps
    // its SRID without Microsoft.SqlServer.Types. Its stored encoding can't be sent instead: SQL
    // Server trusts that encoding's validity flag (an invalid polygon flagged valid reported area 0;
    // one flagged invalid refused STArea), confirmed live on SQL Server 2025.
    public override bool RendersColumnArgument(IColumnInfo column) =>
        SpatialConstructor(column) != null || base.RendersColumnArgument(column);

    public override string RenderColumnArgument(string parameterMarker, IColumnInfo column)
    {
        var type = SpatialConstructor(column);
        if (type == null)
        {
            return base.RenderColumnArgument(parameterMarker, column);
        }

        // A NULL value binds as DbType.Object, which SqlClient sends as sql_variant: SUBSTRING rejects
        // a sql_variant and STGeomFromWKB a NULL argument (both confirmed live), hence the cast and
        // the CASE.
        var bytes = string.Concat("CAST(", parameterMarker, " AS varbinary(max))");
        return string.Concat("CASE WHEN ", parameterMarker, " IS NULL THEN NULL ELSE ", type,
            "::STGeomFromWKB(SUBSTRING(", bytes, ", 5, DATALENGTH(", parameterMarker, ")), CAST(SUBSTRING(", bytes,
            ", 1, 4) AS int)) END");
    }

    private static string? SpatialConstructor(IColumnInfo column)
    {
        var type = column.PropertyInfo.PropertyType;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (typeof(Geography).IsAssignableFrom(type))
        {
            return "geography";
        }

        return typeof(Geometry).IsAssignableFrom(type) ? "geometry" : null;
    }

    private void AppendBatchMerge(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo>? columns)
    {
        if (rowCount <= 0)
        {
            return;
        }

        // SQL Server MERGE pattern:
        // MERGE INTO target AS t
        // USING (VALUES (@b0, @b1), (@b2, @b3)) AS s(pk, col1)
        // ON t.pk = s.pk
        // WHEN MATCHED THEN UPDATE SET col1 = s.col1, ...;

        query.Append("MERGE INTO ");
        query.Append(tableName);
        query.Append(" AS t USING (VALUES ");

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
                    AppendBatchValue(query, columns, col, paramIdx++);
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

        query.Append(") ON (");
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

        query.Append(") WHEN MATCHED THEN UPDATE SET ");
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

        query.Append(';');
    }

    // Version-specific overrides
    public override bool SupportsMerge => IsVersionAtLeast(10);
    public override bool SupportsJsonTypes => IsVersionAtLeast(13);
    public override bool SupportsSavepoints => true;

    // T-SQL's SAVE TRANSACTION has no explicit release statement at all — a savepoint is
    // implicitly valid until superseded by a later one with the same name or the transaction
    // ends, so there is nothing to call ReleaseSavepointAsync against.
    public override SavepointCapabilities SavepointCapabilities =>
        SavepointCapabilities.Create | SavepointCapabilities.Rollback;

    // SQL Server uses SAVE TRANSACTION / ROLLBACK TRANSACTION instead of SAVEPOINT
    public override string GetSavepointSql(string name)
    {
        return $"SAVE TRANSACTION {WrapObjectName(name)}";
    }

    public override string GetRollbackToSavepointSql(string name)
    {
        return $"ROLLBACK TRANSACTION {WrapObjectName(name)}";
    }

    public override bool SupportsInsertReturning => true;
    public override bool SupportsIdentityColumns => true;

    public override bool InsertReturningClauseBeforeValues => true;

    // A plain "OUTPUT INSERTED.col" is rejected when the target table has an enabled trigger. The
    // OUTPUT ... INTO @table-variable form works whether or not a trigger is present, so it is used
    // unconditionally rather than detecting triggers at SQL-generation time.
    public override (string Prefix, string Output, string Returning) RenderOutputInsertClauses(string idWrapped,
        string clause)
    {
        const string outputTable = "@__pengdows_output";
        return ($"DECLARE {outputTable} TABLE ({idWrapped} sql_variant); ",
            $"{clause} INTO {outputTable} ({idWrapped})",
            $"; SELECT {idWrapped} FROM {outputTable}");
    }

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"OUTPUT INSERTED.{WrapObjectName(idColumnName)}";
    }

    public override string RenderInsertReturningClause(string idColumnWrapped)
    {
        return $" OUTPUT INSERTED.{idColumnWrapped}";
    }

    public override string GetLastInsertedIdQuery()
    {
        // Fallback method - prefer OUTPUT clause
        return "SELECT SCOPE_IDENTITY()";
    }

    public override string GetVersionQuery()
    {
        return "SELECT @@VERSION";
    }

    public override async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        var result = await ExecuteScalarQueryAsync(
                connection,
                GetVersionQuery(),
                static value => value?.ToString() ?? string.Empty,
                ex => $"Error retrieving version: {ex.Message}")
            .ConfigureAwait(false);

        return result ?? string.Empty;
    }

    public override string GetBaseSessionSettings()
    {
        // null means detection never ran (e.g. DetectDatabaseInfoAsync was never called before
        // checkout) — safe default is the full baseline. An empty string, by contrast, is a
        // DELIBERATE outcome of GetSqlServerSessionSettings' live compatibility-level check
        // (modern engine confirmed, no SET script needed) and must be honored as-is here, not
        // coerced back to the baseline.
        // SQL Server uses ApplicationIntent=ReadOnly in the connection string for read-only.
        return _sessionSettings ?? DefaultSessionSettings;
    }

    public override string? GetReadOnlyConnectionParameter()
    {
        // NOTE: ApplicationIntent=ReadOnly is a routing hint for Availability Groups (AG)
        // and does NOT enforce server-side read-only state. Hard enforcement requires 
        // read-only credentials or database permissions.
        return "ApplicationIntent=ReadOnly";
    }

    public override bool IsReadCommittedSnapshotOn(ITrackedConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RcsiQuery;
        var val = cmd.ExecuteScalar();
        var v = val is int i ? i : Convert.ToInt32(val ?? 0);
        return v == 1;
    }

    public override bool IsSnapshotIsolationOn(ITrackedConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SnapshotIsolationQuery;
        var value = cmd.ExecuteScalar();
        var state = value is int i
            ? i
            : Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture);
        return state == 1;
    }

    // SQL Server uses base class ApplyConnectionSettings implementation

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);

        // Check and cache SQL Server session settings during initialization
        if (_sessionSettings == null)
        {
            var result = GetSqlServerSessionSettings(connection);
            _sessionSettings = result.Settings;

            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying SQL Server session settings on first connect:\n{Settings}",
                    _sessionSettings);
            }
            else
            {
                Logger.LogInformation(
                    "SQL Server session settings: already compliant; enforcing baseline on every checkout");
            }
        }

        return productInfo;
    }

    // Threshold below which ANSI_WARNINGS ON no longer implicitly promotes ARITHABORT to
    // effectively ON. Unreachable on
    // any SQL Server version this dialect targets — kept only as the gate for the defensive
    // fallback below.
    private const int MinimumCompatibilityLevelForImplicitArithAbort = 90;

    private const string CompatibilityLevelQuery =
        "SELECT compatibility_level FROM sys.databases WHERE database_id = DB_ID()";

    private SessionSettingsResult GetSqlServerSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                var compatibilityLevel = TryGetCompatibilityLevel(conn);

                // Modern compatibility level: the driver's login sequence and
                // sp_reset_connection already guarantee everything this baseline would assert.
                // No SET script needed.
                if (compatibilityLevel is >= MinimumCompatibilityLevelForImplicitArithAbort)
                {
                    return new SessionSettingsResult(string.Empty, ExpectedSessionSettings, false);
                }

                // Compatibility level could not be determined, or is genuinely pre-2005: fall
                // back to the full legacy baseline — exactly the scenario it exists to protect.
                return new SessionSettingsResult(DefaultSessionSettings, ExpectedSessionSettings, false);
            },
            () => new SessionSettingsResult(
                DefaultSessionSettings,
                new Dictionary<string, string>(ExpectedSessionSettings, StringComparer.OrdinalIgnoreCase),
                true),
            "Failed to configure SQL Server session settings");
    }

    private int? TryGetCompatibilityLevel(IDbConnection connection)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = CompatibilityLevelQuery;
            return cmd.ExecuteScalar() switch
            {
                byte b => b,
                short s => s,
                int i => i,
                long l => (int)l,
                _ => null
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "SQL Server compatibility-level probe failed; the level stays unknown.");
            return null;
        }
    }

    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>
        {
            { 13, SqlStandardLevel.Sql2016 }, // SQL Server 2016+
            { 12, SqlStandardLevel.Sql2011 }, // SQL Server 2014
            { 10, SqlStandardLevel.Sql2008 }, // SQL Server 2008+
            { 8, SqlStandardLevel.Sql2003 } // SQL Server 2000+
        };
    }

    public override SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql2008;
    }

    // Connection pooling properties for SQL Server
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";
    public override string? ApplicationNameSettingName => "Application Name";

    public override bool IsUniqueViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) is 2601 or 2627;

    // "FOREIGN KEY constraint" wording covers INSERT/UPDATE blocked by a missing parent row;
    // "REFERENCE constraint" wording covers DELETE blocked by an existing child row — both are
    // real SQL Server 547 message shapes for the same underlying constraint family (confirmed
    // live: "The DELETE statement conflicted with the REFERENCE constraint ..."). Missing the
    // second form was a live-caught regression during the exception-classification unification
    // that made SqlServerExceptionTranslator delegate here instead of unconditionally assuming
    // FK for any 547 that wasn't CHECK.
    public override bool IsForeignKeyViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) == 547 &&
        (ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
         ex.Message.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase));

    public override bool IsNotNullViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) == 515;

    public override bool IsCheckConstraintViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) == 547 &&
        ex.Message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase);

    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        var errorCode = TryGetProviderErrorCode(ex);

        if (errorCode == 1205)
        {
            category = DbErrorCategory.Deadlock;
            return true;
        }

        if (errorCode == 3960)
        {
            category = DbErrorCategory.SerializationFailure;
            return true;
        }

        if (errorCode == -2)
        {
            category = DbErrorCategory.Timeout;
            return true;
        }

        // Checked as a generic category-level fallback, distinct from IsUniqueViolation/
        // IsForeignKeyViolation/IsNotNullViolation/IsCheckConstraintViolation (already checked
        // earlier in SqlDialect.ClassifyException, before this method is ever called) — 547 is
        // shared by both FK and CHECK violations there, disambiguated only by specific message
        // wording ("FOREIGN KEY"/"REFERENCE constraint" vs "CHECK constraint"), so a 547 (or
        // 515/2601/2627) that doesn't match either message shape still needs to register as a
        // constraint violation at the category level even though Translate's kind-specific
        // dispatch cannot name which kind it is.
        if (errorCode is 515 or 547 or 2601 or 2627)
        {
            category = DbErrorCategory.ConstraintViolation;
            return true;
        }

        category = DbErrorCategory.Unknown;
        return false;
    }
}
