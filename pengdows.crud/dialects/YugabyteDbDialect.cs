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
using pengdows.crud.exceptions.translators;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// YugabyteDB dialect inheriting from PostgreSQL for distributed SQL compatibility.
/// </summary>
internal class YugabyteDbDialect : PostgreSqlDialect
{
    internal override int? DefaultServerPort => 5433;

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
        return base.GetBaseSessionSettings() + DistributedSessionSettings;
    }

    /// <summary>
    /// Disables Npgsql auto-prepare for YugabyteDB.
    /// PostgreSqlDialect sets MaxAutoPrepare=64 which causes Npgsql to persist prepared
    /// statements across pool checkout cycles. YugabyteDB does not reliably preserve these
    /// prepared statement handles after a transaction commits and the connection resets,
    /// causing Npgsql to treat the connection as broken ("Connection is not open").
    /// Setting MaxAutoPrepare=0 disables this behavior entirely.
    /// </summary>
    internal override string PrepareConnectionStringForDataSource(string connectionString, bool readOnly = false)
    {
        // Do NOT set _settingsBaked = true here.
        // YugabyteDB does not bake session settings into startup Options — only disables
        // MaxAutoPrepare. Session settings (client_encoding, lock_timeout) must be applied
        // via SET commands on every checkout. The PostgreSQL startup-Options optimization
        // (which eliminates per-checkout SET round-trips) does not apply to YugabyteDB.
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

    // REV-039: YugabyteDB shares PostgreSQL's type mappings, hstore and Npgsql value types. Its
    // IntervalYearMonth/IntervalDaySecond values have never been sent as ISO 8601 text the way
    // PostgreSQL's and CockroachDB's are; that is kept as it was.
    internal static DatabaseTraits CreateYugabyteDbTraits() =>
        new(SupportedDatabase.YugabyteDb, PostgreSqlFamilyExceptionTranslator)
        {
            SpatialFormat = SpatialWireFormat.ExtendedWkb,
            BindsNpgsqlValueTypes = true,
            RegisterTypeMappings = registry =>
            {
                RegisterPostgreSqlFamilyTypeMappings(registry, SupportedDatabase.YugabyteDb);
                RegisterHStoreMapping(registry, SupportedDatabase.YugabyteDb);
            }
        };
}
