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
//     QUOTED_IDENTIFIER is forced ON via session settings; the base-class
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

    // SQL Server uses OFFSET/FETCH NEXT syntax only — no LIMIT keyword.
    public override bool SupportsLimitOffset => false;
    public override string ParameterMarker => "@";

    // DO NOT override QuotePrefix / QuoteSuffix here.
    // We enforce SET QUOTED_IDENTIFIER ON (see SessionSettingsDef) on every
    // connection, so identifiers are quoted with ANSI double-quotes ("name").
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

    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    // IMMUTABLE: SQL Server VALUES clause limit - do not change without extensive testing
    public override int MaxRowsPerBatch => 1000;

    public override bool SupportsBatchUpdate => true;

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
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

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"OUTPUT INSERTED.{WrapObjectName(idColumnName)}";
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
        // Always enforce the full baseline on every connection checkout.
        // A cached empty diff means the first sampled connection was already compliant,
        // but pooled connections can drift if external code mutates session state.
        // SQL Server uses ApplicationIntent=ReadOnly in the connection string for read-only.
        return string.IsNullOrWhiteSpace(_sessionSettings) ? DefaultSessionSettings : _sessionSettings;
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

    private SessionSettingsResult GetSqlServerSessionSettings(IDbConnection connection)
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
                new Dictionary<string, string>(ExpectedSessionSettings, StringComparer.OrdinalIgnoreCase),
                true),
            "Failed to configure SQL Server session settings");
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
}