// =============================================================================
// FILE: YugabyteDbDialect.cs
// PURPOSE: YugabyteDB specific dialect implementation.
//
// AI SUMMARY:
// - Inherits from PostgreSqlDialect for high compatibility.
// - Supports YugabyteDB's distributed PostgreSQL (YSQL).
// - Identifies itself via the "YB" string in the version information.
// - Disables Npgsql auto-prepare (MaxAutoPrepare=0) because YugabyteDB
//   does not reliably preserve prepared statements across pool checkout cycles,
//   causing "Connection is not open" errors after transactions complete.
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// YugabyteDB dialect inheriting from PostgreSQL for distributed SQL compatibility.
/// </summary>
internal class YugabyteDbDialect : PostgreSqlDialect
{
    internal YugabyteDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger, SupportedDatabase.YugabyteDb)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.YugabyteDb;

    // YugabyteDB (YSQL) is built on PostgreSQL 11+ and supports most PG features.
    // It has specific performance characteristics for distributed primary keys.

    // Disable manual cmd.Prepare() calls.
    public override bool PrepareStatements => false;

    // YugabyteDB 2.x does not implement the SQL:2016 MERGE statement despite being based on
    // PostgreSQL 15 (which does). The version string "PostgreSQL 15.x-YB-..." would normally
    // trigger SupportsMerge=true via IsVersionAtLeast(15), but MERGE is unimplemented and
    // throws 0A000 "This statement not supported yet". Force INSERT ON CONFLICT path instead.
    public override bool SupportsMerge => false;

    public override string GetBaseSessionSettings()
    {
        return $"{base.GetBaseSessionSettings()}\nSET client_encoding = 'UTF8';\nSET lock_timeout = '30s';";
    }

    /// <summary>
    /// Disables Npgsql auto-prepare for YugabyteDB.
    /// PostgreSqlDialect sets MaxAutoPrepare=64 which causes Npgsql to persist prepared
    /// statements across pool checkout cycles. YugabyteDB does not reliably preserve these
    /// prepared statement handles after a transaction commits and the connection resets,
    /// causing Npgsql to treat the connection as broken ("Connection is not open").
    /// Setting MaxAutoPrepare=0 disables this behavior entirely.
    /// </summary>
    internal override string PrepareConnectionStringForDataSource(string connectionString)
    {
        try
        {
            ConnectionStringBuilder.ConnectionString = connectionString;
            var builder = ConnectionStringBuilder;
            var modified = false;

            // Disable Npgsql auto-prepare — YugabyteDB cannot reliably persist prepared
            // statements across pool reset cycles.
            if (!builder.ContainsKey("MaxAutoPrepare") || (int)builder["MaxAutoPrepare"] != 0)
            {
                builder["MaxAutoPrepare"] = 0;
                modified = true;
            }

            // Multiplexing must remain off (inherited behavior).
            if (!builder.ContainsKey("Multiplexing") || (bool)builder["Multiplexing"])
            {
                builder["Multiplexing"] = false;
                modified = true;
            }

            return modified ? builder.ConnectionString : connectionString;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to prepare YugabyteDB connection string for DataSource.");
            return connectionString;
        }
    }
}
