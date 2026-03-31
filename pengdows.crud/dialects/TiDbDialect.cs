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

    // TiDB does not enforce FK constraints by default (compatibility mode).
    // TiDB parses CHECK constraint DDL but does not enforce it at runtime.
    public override bool EnforcesForeignKeyConstraints => false;
    public override bool SupportsCheckConstraints => false;

    public override string GetBaseSessionSettings()
    {
        return string.Concat(base.GetBaseSessionSettings(), "\nSET tidb_pessimistic_txn_default = ON;");
    }
}
