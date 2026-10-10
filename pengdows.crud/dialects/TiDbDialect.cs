// =============================================================================
// FILE: TiDbDialect.cs
// PURPOSE: TiDB specific dialect implementation.
//
// AI SUMMARY:
// - Inherits from MySqlDialect for distributed MySQL compatibility.
// - Supports TiDB's distributed transactional SQL.
// - Enables TiDB-specific distributed transaction tuning (e.g., pessimistic mode).
// - Identifies itself via the "TiDB" string in the version information.
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.wrappers;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// TiDB dialect inheriting from MySQL for distributed SQL compatibility.
/// </summary>
internal class TiDbDialect : MySqlDialect
{
    // TiDB's banner ("8.0.11-TiDB-v7.5.0") leads with the MySQL version it imitates; its version
    // gates are keyed to the TiDB release at the end, as before REV-052.
    public override Version? ParseVersion(string versionString) => ParseLastDottedVersion(versionString);

    // TiDB treats READ ONLY as a no-op unless tidb_enable_noop_functions is set (live).
    internal override bool EnforcesReadOnlyTransactions => false;

    internal TiDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.TiDb)
    {
    }

    internal override int? DefaultServerPort => 4000;

    // Not a single server with a fixed connection limit this probe could read: report "unknown"
    // rather than inherit a probe that does not apply.
    internal override Task<int?> ProbeServerConnectionLimitCoreAsync(ITrackedConnection connection, bool useAsync)
        => Task.FromResult<int?>(null);

    public override SupportedDatabase DatabaseType => SupportedDatabase.TiDb;

    // TiDB supports most MySQL features (MySQL 5.7/8.0 wire-compatible)
    // but benefits from a "Pessimistic" transaction mode for correctness
    // in complex distributed workloads.

    // Oracle MySql.Data has a bug/incompatibility with TiDB when preparing statements.
    // MySqlConnector does not have this issue; binary-protocol parameters also avoid
    // the server-side backslash processing that corrupts string values in text protocol.
    public override bool PrepareStatements => _isMySqlConnector;

    // TiDB's Go AST parser does not implement stored procedure DDL (*ast.ProcedureInfo).
    // Stored procedures cannot be created or called on TiDB.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    // TiDB rejects the MySQL 8.0.20+ "INSERT ... AS incoming" row alias (confirmed live on
    // v7.5.1). MySqlDialect gates the alias on the parsed version, and TiDB's parsed version is its
    // own release number, so TiDB v8.x would cross that gate. Always use VALUES(column).
    public override string? UpsertIncomingAlias => null;
    public override string UpsertIncomingColumn(string columnName) => $"VALUES({WrapObjectName(columnName)})";

    // CONFIRMED live (v8.5.7, MySqlConnector 2.4.0/2.6.2, MySql.Data 9.4.0; WRT-009, REV-083): VALUES(col)
    // returns a BIT(64) value byte-reversed whether it is bound as ulong, long or byte[], in single- and
    // multi-row upserts; BIGINT, BIGINT UNSIGNED, BIT(8/16/32), a plain INSERT and "col = @param" are
    // correct, and TiDB has no row alias. A column that could be BIT(64) takes the safe path (DEC-012)
    // unless the gateways have learned it is declared as something else (PERF-029: a batch upsert is
    // then one statement again); any BIT stays safe, since the driver doesn't name the width. Never undo
    // the reversal: that would corrupt data once TiDB fixes it. Key columns are never in the update list.
    internal override bool NeedsDeclaredType(IColumnInfo column, bool forRead) =>
        (!forRead && CouldBeBit64(column) && !column.IsId && !column.IsPrimaryKey) ||
        base.NeedsDeclaredType(column, forRead);

    internal override bool UpsertIncomingValueUnreliable(IColumnInfo column) =>
        CouldBeBit64(column) && (DeclaredTypeOf(column) is not { } declared || SplitDeclaredType(declared).Name == "bit");

    private static bool CouldBeBit64(IColumnInfo column) =>
        column.DbType is DbType.UInt64 or DbType.Int64 or DbType.Binary;

    // TiDB does not enforce FK constraints by default (compatibility mode).
    // TiDB parses CHECK constraint DDL but does not enforce it at runtime.
    public override bool EnforcesForeignKeyConstraints => false;
    public override bool SupportsCheckConstraints => false;

    // MySql.Data substitutes parameters into text-protocol commands using backslash escapes, and
    // TiDB takes those backslashes literally when NO_BACKSLASH_ESCAPES is set (confirmed live:
    // "O'Brien" -> syntax error). TiDB omits the mode for every driver, as 3.0 does.
    protected override bool OmitNoBackslashEscapes => true;

    // TiDB rejects sql_mode TIME_TRUNCATE_FRACTIONAL ("ERROR 1231: Variable 'sql_mode' can't be set
    // to the value of 'TIME_TRUNCATE_FRACTIONAL'", confirmed live on v7.5.1) and rounds fractional
    // seconds: 23:59:59.9999999 stores in TIME as 24:00:00 and in DATETIME as the next day.
    protected override bool SupportsTimeTruncateFractional => false;

    internal override bool RoundsFractionalSecondsOnWrite => true;

    public override string GetBaseSessionSettings()
    {
        return string.Concat(base.GetBaseSessionSettings(), "\nSET tidb_pessimistic_txn_default = ON;");
    }

    // Isolation mapping (DEC-010; was IsolationResolver's per-database switch, same names as 3.0).
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) =>
        new HashSet<IsolationLevel>
        {
            IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead
            // Note: TiDB accepts SERIALIZABLE syntax but silently treats it as REPEATABLE READ.
            // Omitting it prevents callers from relying on semantics that are never enforced.
        };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) =>
        new Dictionary<IsolationProfile, IsolationLevel>
        {
            [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
            [IsolationProfile.StrictConsistency] = IsolationLevel.RepeatableRead, // Best available; TiDB doesn't enforce true Serializable (Degraded)
            [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
        };

    // REV-039: MySQL's JSON mapping; none of MySQL's spatial handling has been keyed on TiDB.
    internal static DatabaseTraits CreateTiDbTraits() =>
        new(SupportedDatabase.TiDb, MySqlFamilyExceptionTranslator)
        {
            RegisterTypeMappings = registry => RegisterJsonMapping(registry, SupportedDatabase.TiDb)
        };
}
