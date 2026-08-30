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
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// TiDB dialect inheriting from MySQL for distributed SQL compatibility.
/// </summary>
internal class TiDbDialect : MySqlDialect
{
    internal TiDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.TiDb)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.TiDb;

    // TiDB accepts SERIALIZABLE syntax but silently treats it as REPEATABLE READ — omitting it
    // prevents callers from relying on semantics that are never enforced. No ReadUncommitted
    // either, unlike the MySqlDialect base this class inherits from.
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) => new()
    {
        IsolationLevel.ReadCommitted,
        IsolationLevel.RepeatableRead
    };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) => new()
    {
        [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
        [IsolationProfile.StrictConsistency] = IsolationLevel.RepeatableRead, // Best available; TiDB doesn't enforce true Serializable
        [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
    };

    // TiDB supports most MySQL features (MySQL 5.7/8.0 wire-compatible)
    // but benefits from a "Pessimistic" transaction mode for correctness
    // in complex distributed workloads.

    // Oracle MySql.Data has a bug/incompatibility with TiDB when preparing statements.
    // Originally suspected (tested at 9.3.0) to be a text-protocol backslash-escaping mismatch
    // corrupting string parameter values. MySqlConnector's binary-protocol parameters avoid this
    // entirely, hence the driver check below.
    //
    // Re-verified 2026-08-30 (see testbed.DriverVersionMatrix/MySqlDataTiDbBackslashCorruptionTests.cs)
    // against a live TiDB container across MySql.Data 9.3.0, 9.4.0, and 9.7.0 (the newest
    // available on NuGet at the time) — the real failure is more fundamental and MORE severe
    // than originally documented, and identical across all three versions spanning nearly two
    // years of releases: MySqlCommand.Prepare() itself throws an unhandled KeyNotFoundException
    // against TiDB (MySqlField.SetFieldEncoding's character-set-index lookup doesn't recognize
    // the charset ID TiDB's handshake reports) before any parameter value is ever sent — the
    // backslash-corruption scenario below is never actually reached; Prepare() crashes outright
    // first. This workaround is confirmed still necessary, and has been continuously since at
    // least 9.3.0 — this is not a since-fixed-upstream issue.
    //
    // Originally found empirically via this project's own TiDB integration testing
    // (testbed/TiDB/), not from a public tracked issue — a search for a matching upstream bug
    // report in mysql/mysql-connector-net or pingcap/tidb (2026-08-13) did not turn one up.
    public override bool PrepareStatements => _isMySqlConnector;

    // MySql.Data substitutes parameters into text-protocol commands using backslash
    // escapes. TiDB receives those backslashes literally when NO_BACKSLASH_ESCAPES is
    // enabled, so quoted strings (including JSON text) are corrupted. Keep the shared
    // MySQL baseline but omit that mode for TiDB; MySQL Connector/J-style escaping is
    // then interpreted correctly by TiDB.
    public override string GetFinalSessionSettings(bool readOnly)
    {
        return base.GetFinalSessionSettings(readOnly)
            .Replace(",NO_BACKSLASH_ESCAPES", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // TiDB's Go AST parser does not implement stored procedure DDL (*ast.ProcedureInfo).
    // Stored procedures cannot be created or called on TiDB.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    // TiDB's MySQL compatibility layer does not support the MySQL 8.0.20+ "AS incoming"
    // table alias on INSERT ... VALUES. Standard VALUES(column) syntax is required.
    public override string? UpsertIncomingAlias => null;
    public override string UpsertIncomingColumn(string columnName) => $"VALUES({WrapObjectName(columnName)})";

    // TiDB does not enforce FK constraints by default (compatibility mode).
    // TiDB parses CHECK constraint DDL but does not enforce it at runtime.
    public override bool EnforcesForeignKeyConstraints => false;
    public override bool SupportsCheckConstraints => false;

    public override string GetBaseSessionSettings()
    {
        var baseline = base.GetBaseSessionSettings()
            .Replace(",NO_BACKSLASH_ESCAPES", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.Concat(baseline, "\nSET tidb_pessimistic_txn_default = ON;");
    }
}
