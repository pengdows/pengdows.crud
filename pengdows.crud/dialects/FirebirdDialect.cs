// =============================================================================
// FILE: FirebirdDialect.cs
// PURPOSE: Firebird Database specific dialect implementation.
//
// AI SUMMARY:
// - Supports Firebird 3.0+ with version-specific features.
// - Key features:
//   * MERGE statement support
//   * Parameter marker: @ (at sign)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 65535 (theoretical limit)
//   * EXECUTE PROCEDURE for stored proc calls
// - Window functions support in Firebird 3.0+.
// - RETURNING clause for getting generated IDs.
// - Generator (sequence) based ID generation.
// - Embedded and server modes supported.
// - Pool separation / read-only enforcement (2026-09-18, CONFIRMED LIVE against a real
//   firebirdsql/firebird:5.0.2 container): this dialect previously had ZERO coverage on
//   ApplicationNameSettingName, GetReadOnlyConnectionParameter, ReadOnlyPoolDiscriminatorSettingName,
//   or TryEnterReadOnlyTransaction, unlike every other long-established client-server dialect —
//   meaning Firebird's reader and writer connections shared one physical pool with no database-
//   level read-only enforcement at all (only pengdows.crud's own SqlContainer pre-flight check).
//     * ApplicationName IS real and confirmed (see ApplicationNameSettingName below) — fixes pool
//       separation.
//     * Pooling: FbConnectionStringBuilder.Pooling (bool, default True) matches SqlDialect's base
//       "Pooling" keyword exactly (confirmed via reflection) — no PoolingSettingName override
//       needed, this was already correct by inheritance.
//     * No connection-string-level read-only property exists — FbConnectionStringBuilder.IsReadOnly
//       is confirmed (via DeclaredOnly reflection) to be the INHERITED base
//       System.Data.Common.DbConnectionStringBuilder.IsReadOnly (the "is this builder locked"
//       indicator, no setter, no effect on ConnectionString text), not a real Firebird keyword —
//       the same trap InterBaseDialect's "IsReadOnly" investigation hit.
//     * A genuine session/transaction-level read-only mechanism DOES exist at the driver level —
//       CONFIRMED LIVE: FbConnection.BeginTransaction(new FbTransactionOptions { TransactionBehavior
//       = FbTransactionBehavior.Read | FbTransactionBehavior.Concurrency | FbTransactionBehavior.Wait })
//       correctly rejects a write ("FbException: attempted update during read-only transaction")
//       while reads succeed normally. The Oracle/Informix-style mid-transaction SQL statement
//       ("SET TRANSACTION READ ONLY" issued after BeginTransaction) was tried first and CONFIRMED
//       to fail ("FbException: invalid transaction handle (expecting explicit transaction start)")
//       — Firebird's TPB (Transaction Parameter Block) model, like InterBase's identical one,
//       requires the read-only flag at transaction-CREATION time, not as a follow-up statement.
//       This does NOT fit ISqlDialect.TryEnterReadOnlyTransaction's hook, which only runs after
//       TransactionContext has already opened the transaction via the ordinary IsolationLevel-based
//       BeginTransaction() overload. Implementing this for real needs the same new pengdows.crud
//       extension point already identified for InterBaseDialect (a dialect controlling transaction
//       *creation* itself, not just post-begin SQL) — left unimplemented here for the identical
//       reason, rather than forcing a broken partial fix. See
//       docs/connection/new-database-pooling-appname-readonly-audit.md's Firebird section.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Controls how <see cref="Guid"/> values are stored in Firebird parameters.
/// </summary>
/// <remarks>
/// Firebird has no native UUID column type prior to Firebird 5.0.
/// Existing schemas typically store GUIDs as CHAR(16) OCTETS (binary) or CHAR(36)/VARCHAR(36) (string).
/// Choose the mode that matches your schema's column type to avoid silent data-format mismatches.
/// </remarks>
internal enum FirebirdGuidStorageMode
{
    /// <summary>
    /// Store GUID as <see cref="DbType.Binary"/> (16-byte CHAR OCTETS).
    /// <para>Default and backward-compatible with existing Firebird schemas.</para>
    /// </summary>
    Binary,

    /// <summary>
    /// Store GUID as <see cref="DbType.String"/> (36-character hyphenated UUID string).
    /// <para>Opt-in for new schemas that use VARCHAR(36) or CHAR(36) UUID columns.</para>
    /// <para><strong>WARNING:</strong> Switching an existing database from Binary to String
    /// is a data-migration event. Round-trip correctness depends on the column type matching
    /// this setting.</para>
    /// </summary>
    String
}

/// <summary>
/// Firebird Database dialect with SQL standard compliance.
/// </summary>
/// <remarks>
/// <para>
/// Supports Firebird 3.0 and later with automatic version detection.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement.
/// </para>
/// <para>
/// <strong>ID Generation:</strong> Uses generators (sequences) with
/// RETURNING clause to fetch generated values.
/// </para>
/// </remarks>
internal class FirebirdDialect : SqlDialect
{
    internal FirebirdDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Firebird;

    // CONFIRMED LIVE (2026-09-18, real firebirdsql/firebird:5.0.2 container): ApplicationName is
    // a real, working property on FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder —
    // round-trips to "application name=..." in the connection string, and a live connection with
    // it set succeeds normally. Without this set, reader/writer connection strings would be
    // identical and collapse into one shared pool — the same bug class Access/Db2/Sybase/
    // InterBase/Informix's ApplicationNameSettingName fixes addressed this session. See the
    // "Read-only enforcement" remarks below for the companion finding.
    //
    // KNOWN INTERACTION, found and fixed the same session this was added: enabling this setting
    // means reader and writer connections now use genuinely DISTINCT connection strings — and
    // therefore genuinely DISTINCT physical ADO.NET pools — for the first time. RequiresConnection-
    // PoolResetForDdl/ResetConnectionPoolForDdl below existed before this change but only ever
    // cleared ONE pool (the DDL statement's own, i.e. the writer's), which was sufficient when
    // reader and writer shared one pool. Once they diverged, a stale idle connection sitting in
    // the now-separate READER pool could still block a DDL commit on the WRITER connection with
    // the identical "object TABLE ... is in use" failure this hook exists to prevent — reproduced
    // live via CompositeKeyTests failing on the very first DDL statement against a brand-new
    // container (not an accumulation-over-many-operations effect). Fixed generically in
    // SqlContainer.ExecuteNonQueryAsync's DDL-reset call site, which now resets both the writer's
    // and reader's raw connection string whenever they differ — not a Firebird-specific patch,
    // since the same divergence applies to any future RequiresConnectionPoolResetForDdl dialect.
    public override string? ApplicationNameSettingName => "Application Name";

    // Firebird: "violation of PRIMARY OR UNIQUE KEY constraint <name> on table <table>"
    public override bool IsUniqueViolation(DbException ex) =>
        ex.Message.Contains("violation of PRIMARY", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("violation of UNIQUE", StringComparison.OrdinalIgnoreCase);

    // IsForeignKeyViolation/IsCheckConstraintViolation are NOT overridden here — Firebird's real
    // messages ("violation of FOREIGN KEY constraint...", "check constraint failed") already
    // match the base class's generic message-based default.

    // Firebird: "validation error for column X, value \"*** null ***\""
    public override bool IsNotNullViolation(DbException ex) =>
        ex.Message.Contains("*** null ***", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("not-null", StringComparison.OrdinalIgnoreCase);

    public override string RenderInsertReturningClause(string idColumnWrapped) =>
        $" RETURNING {idColumnWrapped}";

    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        var sqlState = TryGetProviderSqlState(ex);

        // Firebird cannot distinguish a true lock-cycle deadlock from an optimistic update
        // conflict — confirmed against a live container, both scenarios produce the identical
        // SQLSTATE 40001 / "update conflicts with concurrent update" signature. Classified as
        // SerializationFailure here, matching the same ambiguous-40001 precedent used for Db2.
        if (string.Equals(sqlState, "40001", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("update conflicts with concurrent update", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.SerializationFailure;
            return true;
        }

        // Firebird 3+ uses SQLSTATE class 23 for all integrity constraint violations, with a
        // message fallback for drivers that don't populate SqlState. Checked as a generic
        // category-level fallback, distinct from IsUniqueViolation/IsForeignKeyViolation/
        // IsNotNullViolation/IsCheckConstraintViolation (already checked earlier in
        // SqlDialect.ClassifyException, before this method is ever called) — those four are all
        // message-substring based and each need specific wording to identify ONE kind, so a bare
        // SqlState with no matching message text (or an empty message) still needs to register as
        // a constraint violation at the category level even though Translate's kind-specific
        // dispatch cannot name which kind it is.
        if (!string.IsNullOrWhiteSpace(sqlState) && sqlState.StartsWith("23", StringComparison.Ordinal))
        {
            category = DbErrorCategory.ConstraintViolation;
            return true;
        }

        if (ex.Message.Contains("violation of", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("*** null ***", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.ConstraintViolation;
            return true;
        }

        category = DbErrorCategory.Unknown;
        return false;
    }

    // Deliberately does NOT override CoerceConnectionMode, so Best resolves to Standard here —
    // same as any other full server database (base SqlDialect.CoerceConnectionMode). This is a
    // considered choice, not an oversight: Firebird's default SuperServer RDB$LINGER=0 does cause a
    // real, empirically-confirmed reconnect cost after an idle period
    // (testbed.TestProvider.TestIdleUnloadProbe; ~5-10x cold/warm latency ratio against live
    // 3.0/4.0/5.0 containers), and PreventDatabaseUnload genuinely mitigates it (confirmed by the
    // same probe's sentinel-validation follow-up). But unlike SQL Server LocalDB, the cost only
    // matters for deployments with real idle gaps — a heavily-trafficked Firebird instance may
    // never actually drain its pool to zero, making the sentinel's permanent permit cost pure
    // overhead, and forcing it on anyone deliberately running a scale-to-zero/cost-optimized
    // deployment would be exactly the wrong call. Only the operator knows which situation applies,
    // so PreventDatabaseUnload stays a fully-supported, explicitly-honored KNOB (same treatment
    // given to Db2's implicit-activation lifecycle and SQL Server's opt-in AUTO_CLOSE) rather than
    // an auto-selected default — see docs/connection/connection-modes.md and CLAUDE.md's
    // "Connection Management and DbMode" section for the full policy, and CLAUDE.md's warning
    // against casually extending Best's auto-selection list without the same empirical bar.

    protected override bool NeedsCommonConversions => true;

    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => new()
    {
        IsolationLevel.ReadCommitted,
        IsolationLevel.Snapshot,
        IsolationLevel.Serializable
    };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => new()
    {
        [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.Snapshot,
        [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
        [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
    };

    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;

    public override bool SupportsSavepoints => true;

    // IMMUTABLE: Firebird theoretical parameter limit - do not change without extensive testing
    public override int MaxParameterLimit => 65535;

    // IMMUTABLE: Firebird PSQL practical output parameter limit - do not change without extensive testing
    public override int MaxOutputParameters => 1499;

    // IMMUTABLE: Firebird identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 63;

    // Firebird provider can be overly strict during explicit prepare; defer to execution-time preparation
    public override bool PrepareStatements => false;
    public override bool SupportsReadOnlyTransactions => true;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.ExecuteProcedure;

    /// <summary>
    /// Controls how <see cref="Guid"/> values are stored in Firebird parameters.
    /// Defaults to <see cref="FirebirdGuidStorageMode.Binary"/> for backward compatibility.
    /// </summary>
    /// <remarks>
    /// Set to <see cref="FirebirdGuidStorageMode.String"/> only for new schemas that store UUIDs
    /// as CHAR(36)/VARCHAR(36). Changing this setting on an existing database requires a data migration.
    /// </remarks>
    public FirebirdGuidStorageMode GuidStorageMode { get; init; } = FirebirdGuidStorageMode.Binary;

    /// <summary>
    /// Folds <see cref="GuidStorageMode"/> into the base <see cref="SqlDialect.CacheFingerprint"/>.
    /// The base fingerprint is only <c>DatabaseType|ParsedVersion</c>, which is not enough to
    /// distinguish two Firebird tenants on the identical server version but different
    /// <see cref="GuidStorageMode"/> — a real per-instance setting that changes GUID wire format.
    /// Any future cache keyed by fingerprint instead of dialect instance must not collide those.
    /// </summary>
    public override string CacheFingerprint => $"{base.CacheFingerprint}|{GuidStorageMode}";

    /// <summary>
    /// Routes Guid serialization through the centralized <see cref="SqlDialect.GuidFormat"/> path,
    /// computed from <see cref="GuidStorageMode"/> so a single property controls both the
    /// dialect-level wire format and the legacy public API.
    /// </summary>
    protected override GuidStorageFormat GuidFormat =>
        GuidStorageMode == FirebirdGuidStorageMode.Binary
            ? GuidStorageFormat.Binary
            : GuidStorageFormat.String;

    /// <summary>
    /// Serializes a <see cref="Guid"/> to 16 bytes in RFC 4122 big-endian byte order.
    /// The Firebird .NET driver reads <c>CHAR(16) CHARACTER SET OCTETS</c> back as a
    /// <see cref="Guid"/> by treating the stored bytes as a big-endian UUID, so we must
    /// write in that same layout for a correct round-trip.
    /// .NET's <see cref="Guid.ToByteArray()"/> uses mixed-endian (Data1/2/3 little-endian),
    /// so we swap the first three components after calling it.
    /// </summary>
    protected override byte[] SerializeGuidAsBinary(Guid guid)
    {
        var bytes = guid.ToByteArray();
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
        return bytes;
    }

    public override bool SupportsMerge => IsInitialized && ProductInfo.ParsedVersion?.Major >= 2;

    // Firebird's SupportsMerge above is true because it satisfies the merge-family upsert
    // dispatch, but the actual syntax is UPDATE OR INSERT MATCHING, not ANSI MERGE — it doesn't
    // give the same 0-rows-affected-means-conflict guarantee, and it has no UPDATE/SET-clause
    // requirement (unlike everyone else's merge/on-conflict/on-duplicate-key syntax).
    public override bool EmitsAnsiMergeSyntax => false;
    public override bool SupportsPureKeyUpsert => true;

    // Confirmed against a live container: a failed write's implicit transaction otherwise leaves
    // a server-side lock that persists on the pooled connection (not released by Dispose/Close
    // alone), blocking later DDL/DML on the same table from other connections with "lock
    // conflict" / "object ... is in use" until that specific pooled connection happens to be
    // reset or the process exits.
    internal override bool RequiresExplicitRollbackAfterFailedWrite => true;

    internal override bool RequiresConnectionPoolResetForDdl => true;

    // Confirmed against a live container: Firebird's DDL commit additionally requires that no
    // OTHER connection — even one holding only cleanly-committed transactions — still be sitting
    // idle in ANY ADO.NET pool referencing the table's current metadata generation, regardless of
    // which DatabaseContext instance (or connection string) that connection came from.
    // FbConnection.ClearPool(connectionString) — this method's original implementation — only
    // clears the ONE pool keyed by that exact string, which is sufficient for the issuing
    // context's own reader/writer pools (SqlContainer.cs resets both when they differ) but cannot
    // reach a genuinely DIFFERENT DatabaseContext instance's pools, e.g. the second, independent
    // context CreateAdditionalContextAsync-based tests create to simulate a concurrent client.
    // Confirmed live: MergeConflictTests.VersionedEntity_ConcurrentUpdate_DetectsConflict — which
    // does exactly that — still hit "object TABLE ... is in use" under load even after both of
    // the issuing context's own pools were being reset correctly. FbConnection.ClearAllPools()
    // (no parameters, confirmed present via reflection) clears every pool for this provider
    // process-wide, closing that gap — the connectionString parameter becomes irrelevant to which
    // pools get cleared, not merely unused. Called via reflection (no hard package reference from
    // pengdows.crud to FirebirdSql.Data.FirebirdClient) — same pattern as OracleDialect's
    // StatementCacheSize hook.
    internal override void ResetConnectionPoolForDdl(string connectionString)
    {
        try
        {
            using var sampleConnection = Factory.CreateConnection();
            if (sampleConnection == null)
            {
                return;
            }

            var connectionType = sampleConnection.GetType();
            var clearAllPoolsMethod = connectionType.GetMethod(
                "ClearAllPools",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            clearAllPoolsMethod?.Invoke(null, null);
        }
        catch
        {
            // Best-effort — a missed pool reset just means the caller sees the original
            // "object ... is in use" failure, no worse than before this hook existed.
        }
    }

    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 3;
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 2;
    public override bool SupportsJsonTypes => false;
    public override bool SupportsArrayTypes => true;
    public override bool SupportsInsertReturning => true;

    /// <inheritdoc />
    /// <remarks>
    /// Firebird EXECUTE BLOCK requires all external parameters to be declared in the block header
    /// with explicit types. ADO.NET named parameters (@b0, @b1, ...) used in the generated
    /// EXECUTE BLOCK body are not automatically promoted to block-level input parameters,
    /// causing "Dynamic SQL Error -901" at prepare time. Individual INSERTs are used instead.
    /// The <see cref="BuildBatchInsertSql"/> override remains available for callers that generate
    /// the SQL for inspection or non-parameterised use.
    /// </remarks>
    public override bool SupportsBatchInsert => false;

    /// <inheritdoc />
    public override void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
        ISqlQueryBuilder query)
    {
        BuildBatchInsertSql(tableName, columnNames, rowCount, query, null);
    }

    /// <inheritdoc />
    public override void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
        ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name cannot be null or empty.", nameof(tableName));
        }

        if (columnNames == null || columnNames.Count == 0)
        {
            throw new ArgumentException("Column names cannot be null or empty.", nameof(columnNames));
        }

        if (rowCount <= 0)
        {
            throw new ArgumentException("Row count must be greater than zero.", nameof(rowCount));
        }

        // Firebird uses EXECUTE BLOCK AS BEGIN INSERT INTO ...; INSERT INTO ...; END
        query.Append("EXECUTE BLOCK AS BEGIN ");

        var colList = string.Join(", ", columnNames);

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            query.Append("INSERT INTO ");
            query.Append(tableName);
            query.Append(" (");
            query.Append(colList);
            query.Append(") VALUES (");

            for (var col = 0; col < columnNames.Count; col++)
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

            query.Append("); ");
        }

        query.Append("END");
    }

    public override GeneratedKeyPlan GetGeneratedKeyPlan() => GeneratedKeyPlan.Returning;

    public override string? UpsertIncomingAlias => "src";

    public override string UpsertIncomingColumn(string columnName)
    {
        var alias = UpsertIncomingAlias ?? "src";
        var wrappedAlias = WrapObjectName(alias);
        var prefix = string.IsNullOrWhiteSpace(wrappedAlias) ? string.Empty : $"{wrappedAlias}.";
        return $"{prefix}{WrapObjectName(columnName)}";
    }

    public override string GetLastInsertedIdQuery()
    {
        throw new NotSupportedException(
            "Firebird requires generator-specific syntax. Use RETURNING clause or GEN_ID(generator_name, 0) instead.");
    }

    private const string EngineVersionQuery =
        "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database";

    private const string DatabaseInfoQuery = "SELECT * FROM rdb$database";
    private const string MonitorVersionQuery = "SELECT mon$server_version FROM mon$database";

    private const string DefaultNamesAndDialectSettings = "SET NAMES UTF8;\nSET SQL DIALECT 3;";

    public override string GetVersionQuery()
    {
        return EngineVersionQuery;
    }

    // Firebird's "limit to one row" clause is ROWS 1, not the base class's default LIMIT 1.
    // Uses the dedicated hook base.GetNaturalKeyLookupQuery already provides for exactly this
    // (see OracleDialect's own FETCH FIRST override for the same pattern) instead of calling the
    // base implementation and then string-replacing its output — a string-replace depends on the
    // base class's exact generated text staying stable forever, which this hook exists to avoid.
    protected override string GetNaturalKeyFirstRowOnlyClause() => " ROWS 1";

    private string? _sessionSettings;

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);

        if (_sessionSettings == null)
        {
            var result = GetFirebirdSessionSettings(connection);
            _sessionSettings = result.Settings;

            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying Firebird session settings on first connect:\n{Settings}",
                    _sessionSettings);
            }
        }

        return productInfo;
    }

    private SessionSettingsResult GetFirebirdSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                // Always set names to UTF8 and dialect to 3 to ensure deterministic state.
                return new SessionSettingsResult(DefaultNamesAndDialectSettings, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), false);
            },
            () => new SessionSettingsResult(
                DefaultNamesAndDialectSettings,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                true),
            "Failed to configure Firebird session settings");
    }

    public override string GetBaseSessionSettings()
    {
        return _sessionSettings ?? DefaultNamesAndDialectSettings;
    }

    public override string GetReadOnlySessionSettings()
    {
        return "SET TRANSACTION READ ONLY;";
    }

    internal override string? GetReadOnlyTransactionResetSql()
    {
        return "SET TRANSACTION READ WRITE;";
    }

    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        // Prefer scalar results to match fakeDb test helpers
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = EngineVersionQuery;
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result != null)
            {
                return "Firebird";
            }
        }
        catch
        {
            try
            {
                await using var cmd = (DbCommand)connection.CreateCommand();
                cmd.CommandText = DatabaseInfoQuery;
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                if (result != null)
                {
                    return "Firebird";
                }
            }
            catch
            {
                // ignored
            }
        }

        return null;
    }

    public override string ExtractProductNameFromVersion(string versionString)
    {
        return "Firebird";
    }

    public override Version? ParseVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        var standardVersion = base.ParseVersion(versionString);
        if (standardVersion != null)
        {
            return standardVersion;
        }

        var legacyMatch = Regex.Match(versionString, @"LI-V(\d+)\.(\d+)\.(\d+)");
        if (legacyMatch.Success)
        {
            if (int.TryParse(legacyMatch.Groups[1].Value, out var major) &&
                int.TryParse(legacyMatch.Groups[2].Value, out var minor) &&
                int.TryParse(legacyMatch.Groups[3].Value, out var build))
            {
                return new Version(major, minor, build);
            }
        }

        var firebirdMatch = Regex.Match(versionString, @"Firebird\s+(\d+)\.(\d+)");
        if (firebirdMatch.Success)
        {
            if (int.TryParse(firebirdMatch.Groups[1].Value, out var major) &&
                int.TryParse(firebirdMatch.Groups[2].Value, out var minor))
            {
                return new Version(major, minor);
            }
        }

        return null;
    }

    public override async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        // Try engine context first; if returns null or empty, surface empty (do not attempt monitor)
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = EngineVersionQuery;
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            var s = result?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(s))
            {
                return s;
            }

            // If engine query returned null/empty, tests expect an empty string, not a monitor fallback
            return string.Empty;
        }
        catch
        {
            // ignore and try monitor table next
        }

        // Try monitor table; same null/empty handling
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = MonitorVersionQuery;
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            var s = result?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(s))
            {
                return s;
            }
        }
        catch
        {
            // ignore and fall through
        }

        // Both attempts failed (engine threw and monitor had no data)
        return string.Empty;
    }

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        // Intercept unsupported types before they reach the provider parameter's set_DbType
        var targetType = type;
        object? targetValue = value;

        if (type == DbType.Boolean)
        {
            targetType = DbType.Int16;
            if (value is bool boolValue)
            {
                targetValue = boolValue ? (short)1 : (short)0;
            }
        }
        else if (type == DbType.DateTimeOffset)
        {
            // Firebird does not support DateTimeOffset; coerce to UTC DateTime
            targetType = DbType.DateTime;
            if (value is DateTimeOffset dto)
            {
                // Use Unspecified kind to prevent provider-side timezone adjustments
                targetValue = DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Unspecified);
            }
        }

        return base.CreateDbParameter<object?>(name, targetType, targetValue);
    }

    // Connection pooling properties for Firebird
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "MinPoolSize";
    public override string? MaxPoolSizeSettingName => "MaxPoolSize";
}
