// =============================================================================
// FILE: SqlServerDialect.cs
// PURPOSE: SQL Server specific dialect implementation.
//
// AI SUMMARY:
// - Supports SQL Server 2017+ with version-specific feature detection.
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
// - OFFSET/FETCH pagination.
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
/// Supports Microsoft SQL Server 2017 and later with automatic version detection.
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
    //
    // === Investigation trail (2026-09-19/20, live against real SQL Server 2017 AND 2022
    // engines, plus one 2022 instance with database compatibility level manually varied) ===
    //
    // Original cost analysis: under DbMode.Standard AND DbMode.SingleWriter
    // (ConnectionStrategyFactory maps SingleWriter to the same StandardConnectionStrategy,
    // adding only a write-concurrency governor — it does not pin a connection), every operation
    // opens and closes its own ephemeral connection, so this SET batch used to execute as a real,
    // separate round trip on every single operation (~300us/op via
    // DatabaseContext.Metrics.AvgSessionInitMs — roughly the entire pengdows-vs-Dapper
    // ConnectionHoldTime/Update gap seen in SqlServerEqualFootingBenchmarks). Only
    // DbMode.SingleConnection pins one connection for the context's lifetime, so only there did
    // this cost get paid once, not per-operation.
    //
    // Two optimizations were first considered and rejected: (1) trusting driver/reset defaults
    // for the settings that already match, asserting only the one that didn't (ARITHABORT) —
    // rejected as a correctness bet on state pengdows doesn't control; (2) prepending the SET
    // batch to the caller's first command to collapse two round trips into one — rejected because
    // it would pollute every captured statement (Profiler/Extended Events/query store/app logs)
    // forever, undermining "the SQL you see is the SQL that ran" for a marginal win.
    //
    // Then the actual ARITHABORT requirement was investigated directly, because indexed views
    // were the whole reason this setting was in the list in the first place:
    //   - Microsoft's indexed-view documentation lists ARITHABORT ON as required, but ALSO
    //     documents that ANSI_WARNINGS ON implicitly promotes ARITHABORT to effectively ON at
    //     database compatibility level >= 90 — meaning ARITHABORT's own session flag can read
    //     OFF while its functional effect is already ON.
    //   - Confirmed with a live 2x2 matrix (ANSI_WARNINGS x ARITHABORT, each ON/OFF) against a
    //     real schema-bound indexed view: ANSI_WARNINGS is the setting SQL Server actually
    //     enforces — the DML rejection (error 1934) names 'ANSI_WARNINGS' specifically, never
    //     ARITHABORT, and ARITHABORT's own value made zero observable difference to indexed-view
    //     substitution, DML success, or divide-by-zero/overflow handling, as long as
    //     ANSI_WARNINGS stayed ON (which the SqlClient login default already guarantees).
    //   - Confirmed the compat-level threshold is unreachable on any SQL Server version this
    //     dialect targets: SQL Server 2017 AND 2022 both reject
    //     ALTER DATABASE ... SET COMPATIBILITY_LEVEL = 80/90 outright (error 15048, "Valid values
    //     ... are 100, 110, ..."), and the matrix above produced identical results at the lowest
    //     level each engine will accept (100) as at each engine's own current default.
    //   - Confirmed pool-poisoning self-heals regardless: deliberately setting each of the seven
    //     settings to its wrong value mid-session, then opening a new logical connection on the
    //     SAME pooled physical connection (verified via matching ServerProcessId), showed
    //     sp_reset_connection restoring every one of the seven to its own login default, with no
    //     exceptions — not just the ones already investigated.
    //
    // Net conclusion: for every SQL Server version/compatibility level this dialect can actually
    // reach (2017+, compat >= 100), the driver's login sequence plus sp_reset_connection already
    // guarantee all seven settings unconditionally — no explicit SET script is needed at all, and
    // GetSqlServerSessionSettings below skips it entirely once a live compatibility-level check
    // confirms this. The full legacy baseline is kept as a deliberate, narrow safety net — not
    // because it's proven necessary for any officially supported target, but on the chance
    // someone points this dialect at a genuinely old, unsupported SQL Server/database where it
    // still might not hold (this dialect's own documented scope is 2017+; a real SQL Server
    // 2000/2005/2008-era ENGINE, as opposed to an old compat level emulated on a modern engine,
    // was not available to test here — no Docker image exists for anything that old).
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
    /// LocalDB auto-selects PreventDatabaseUnload for <see cref="DbMode.Best"/> and forces it for
    /// every other requested mode EXCEPT an explicit <see cref="DbMode.Standard"/> request, which
    /// is honored as-is. PreventDatabaseUnload's sentinel only matters for a workload that
    /// actually goes idle long enough to trigger LocalDB's auto-shutdown; a caller who knows their
    /// workload stays busy continuously can opt out deliberately (unlike Firebird/Db2's own
    /// idle-unload costs, which stay an explicitly-honored opt-in knob rather than an auto-selected
    /// default — see CLAUDE.md's "Connection Management and DbMode" section for the full policy).
    /// Non-LocalDB SQL Server is an ordinary full server database and falls through to the base
    /// implementation.
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

            return (DbMode.PreventDatabaseUnload, "LocalDB requires PreventDatabaseUnload");
        }

        return base.CoerceConnectionMode(requested, connectionString, isLocalDb);
    }

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
    public override bool SupportsReadOnlyTransactions => true;

    public override bool SupportsNamespaces => true;

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

    protected override GeneratedKeyPlan GetInsertReturningKeyPlan() => GeneratedKeyPlan.OutputInserted;

    // SCOPE_IDENTITY() is per-batch/scope safe.
    public override bool HasSessionScopedLastIdFunction() => true;

    protected override string GetNaturalKeySelectClause(string wrappedIdColumn) => $"SELECT TOP 1 {wrappedIdColumn}";
    protected override string GetNaturalKeyFirstRowOnlyClause() => string.Empty;

    public override string RenderInsertReturningClause(string idColumnWrapped) =>
        $" OUTPUT INSERTED.{idColumnWrapped}";

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

    // IMMUTABLE: SQL Server VALUES clause limit - do not change without extensive testing
    public override int MaxRowsPerBatch => 1000;

    public override bool SupportsBatchUpdate => true;

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        string? versionColumnName = null, bool versionColumnIsOpaque = false)
    {
        if (rowCount <= 0)
        {
            return;
        }

        // SQL Server MERGE pattern:
        // MERGE INTO target AS t
        // USING (VALUES (@b0, @b1, @b2), (@b3, @b4, @b5)) AS s(pk, col1, version)
        // ON t.pk = s.pk AND t.version = s.version
        // WHEN MATCHED THEN UPDATE SET col1 = s.col1, ..., version = version + 1;
        // A version mismatch simply fails to match — no WHEN NOT MATCHED clause exists, so that
        // row is silently skipped rather than erroring, which is exactly what the caller's
        // affected-row-count check needs to detect a stale-version conflict.

        query.Append("MERGE INTO ");
        query.Append(tableName);
        query.Append(" AS t USING (VALUES ");

        var allCols = new List<string>(keyColumns);
        allCols.AddRange(columnNames);
        if (versionColumnName != null)
        {
            allCols.Add(versionColumnName);
        }

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

        if (versionColumnName != null)
        {
            query.Append(" AND t.");
            query.Append(versionColumnName);
            query.Append(" = s.");
            query.Append(versionColumnName);
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

        if (versionColumnName != null && !versionColumnIsOpaque)
        {
            if (columnNames.Count > 0)
            {
                query.Append(", ");
            }

            // RHS must be qualified with the target alias — the USING source's "s" alias also
            // projects a same-named version column (needed for the ON predicate above), so an
            // unqualified "version" reference here is ambiguous (real SQL Server rejects it with
            // "Ambiguous column name 'version'").
            query.Append(versionColumnName);
            query.Append(" = t.");
            query.Append(versionColumnName);
            query.Append(" + 1");
        }

        query.Append(';');
    }

    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) =>
        allowSnapshotIsolation
            ? new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable,
                IsolationLevel.Snapshot
            }
            : new HashSet<IsolationLevel>
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => new()
    {
        [IsolationProfile.SafeNonBlockingReads] = allowSnapshotIsolation
            ? IsolationLevel.Snapshot
            : IsolationLevel.ReadCommitted,
        [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
        [IsolationProfile.FastWithRisks] = IsolationLevel.ReadUncommitted
    };

    // Version-specific overrides
    public override bool SupportsWindowFunctions => !IsInitialized || IsVersionAtLeast(9);
    public override bool SupportsCommonTableExpressions => !IsInitialized || IsVersionAtLeast(9);
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

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"OUTPUT INSERTED.{WrapObjectName(idColumnName)}";
    }

    // SQL Server-specific generated-key protocol: a plain "OUTPUT INSERTED.col" clause breaks
    // when the target table has triggers (the trigger's own DML consumes the OUTPUT result set),
    // so the value is routed through a table variable instead. This lives here — not in generic
    // TableGateway code — because it's SQL Server's own wire protocol, not gateway policy.
    public override (string Prefix, string Output, string Returning) RenderOutputInsertClauses(string idWrapped, string clause)
    {
        const string outputTable = "@__pengdows_output";
        var prefix = $"DECLARE {outputTable} TABLE ({idWrapped} sql_variant); ";
        var output = $"{clause} INTO {outputTable} ({idWrapped})";
        var returning = $"; SELECT {idWrapped} FROM {outputTable}";
        return (prefix, output, returning);
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
        // coerced back to the baseline — see SessionSettingsDef's investigation trail comment.
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

    public override async Task<bool> IsReadCommittedSnapshotOnAsync(ITrackedConnection conn, CancellationToken cancellationToken = default)
    {
        await using var cmd = (DbCommand)conn.CreateCommand();
        cmd.CommandText = RcsiQuery;
        var val = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
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

    public override async Task<bool> IsSnapshotIsolationOnAsync(ITrackedConnection conn, CancellationToken cancellationToken = default)
    {
        await using var cmd = (DbCommand)conn.CreateCommand();
        cmd.CommandText = SnapshotIsolationQuery;
        var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var state = value is int i
            ? i
            : Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture);
        return state == 1;
    }

    // Early, best-effort prefetch performed during DatabaseContext initialization, before this
    // dialect instance is otherwise set up — reuses the same queries as IsReadCommittedSnapshotOn/
    // IsSnapshotIsolationOn above rather than duplicating the SQL text a second time. Each query
    // is independently best-effort: a failure in one must not prevent the other from running.
    internal override SessionCapabilityPrefetch DetectSessionCapabilities(ITrackedConnection connection)
    {
        var rcsi = false;
        var snapshotIsolation = false;

        try
        {
            rcsi = IsReadCommittedSnapshotOn(connection);
        }
        catch
        {
            /* ignore prefetch failures */
        }

        try
        {
            snapshotIsolation = IsSnapshotIsolationOn(connection);
        }
        catch
        {
            /* ignore prefetch failures */
        }

        return new SessionCapabilityPrefetch(rcsi, snapshotIsolation);
    }

    internal override async Task<SessionCapabilityPrefetch> DetectSessionCapabilitiesAsync(ITrackedConnection connection, CancellationToken cancellationToken = default)
    {
        var rcsi = false;
        var snapshotIsolation = false;

        try
        {
            rcsi = await IsReadCommittedSnapshotOnAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            /* ignore prefetch failures */
        }

        try
        {
            snapshotIsolation = await IsSnapshotIsolationOnAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            /* ignore prefetch failures */
        }

        return new SessionCapabilityPrefetch(rcsi, snapshotIsolation);
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
    // effectively ON (see SessionSettingsDef's investigation trail comment above). Unreachable on
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
                // sp_reset_connection already guarantee everything this baseline would assert —
                // see the investigation trail above SessionSettingsDef. No SET script needed.
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

    private static int? TryGetCompatibilityLevel(IDbConnection connection)
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
        catch
        {
            return null;
        }
    }

    // Connection pooling properties for SQL Server
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";
    public override string? ApplicationNameSettingName => "Application Name";
}
