// =============================================================================
// FILE: MariaDbDialect.cs
// PURPOSE: MariaDB specific dialect implementation (extends MySqlDialect).
//
// AI SUMMARY:
// - Inherits from MySqlDialect with MariaDB-specific overrides.
// - Key differences from MySQL:
//   * Read-only session variable: tx_read_only (MariaDB) vs transaction_read_only (MySQL 5.7.20+/8.0+)
//     MySQL 8.0.3 removed tx_read_only; MariaDB never adopted transaction_read_only.
//   * No native JSON type (uses LONGTEXT)
//   * CTEs and window functions in 10.2+ (earlier than MySQL 8.0)
//   * No INSERT ... AS alias syntax for upserts
//   * Prepared statements default ON (vs OFF for MySQL)
//   * OFFSET/FETCH syntax in 10.6+ (MySQL never supported it)
//   * Version numbering is 10.x era (different from MySQL's simple major versioning);
//     DetermineStandardCompliance uses MariaDB-specific major/minor thresholds
// - Uses LAST_INSERT_ID() for returning generated IDs.
// - Session settings: Inherits ANSI_QUOTES mode from MySqlDialect.
// - AUTO_INCREMENT for identity columns.
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// MariaDB dialect inheriting MySQL compatibility with MariaDB-specific differences.
/// </summary>
/// <remarks>
/// <para>
/// MariaDB is a MySQL fork with additional features. This dialect inherits
/// from <see cref="MySqlDialect"/> and overrides only the differences.
/// </para>
/// <para>
/// <strong>Feature Differences:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>Read-only session variable: uses <c>tx_read_only</c> (MariaDB never adopted MySQL's <c>transaction_read_only</c> alias; MySQL 8.0.3 removed <c>tx_read_only</c>)</description></item>
/// <item><description>No native JSON type (mapped to LONGTEXT)</description></item>
/// <item><description>CTEs and window functions available in 10.2+ (vs MySQL 8.0+)</description></item>
/// <item><description>No <c>INSERT ... AS</c> alias syntax for upserts</description></item>
/// <item><description>Prepared statements enabled by default (vs conservative MySQL default)</description></item>
/// <item><description>OFFSET/FETCH syntax supported in 10.6+ (MySQL never supported it)</description></item>
/// <item><description>Version numbering uses 10.x era scheme; <see cref="DetermineStandardCompliance"/> uses MariaDB-specific major/minor thresholds</description></item>
/// </list>
/// </remarks>
internal class MariaDbDialect : MySqlDialect
{
    // MariaDB uses tx_read_only (the original MySQL name); transaction_read_only was added
    // as an alias in MySQL 5.7.20 but MariaDB never adopted it — using it on MariaDB 10.x
    // raises "Unknown system variable 'transaction_read_only'".
    private const string MariaDbReadOnlySql = "SET SESSION tx_read_only = 1;";
    private const string MariaDbReadWriteSql = "SET SESSION tx_read_only = 0;";

    internal MariaDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    internal MariaDbDialect(DbProviderFactory factory, ILogger logger, bool isMySqlConnector)
        : base(factory, logger, isMySqlConnector)
    {
    }

    // Only override what's different from MySQL
    public override SupportedDatabase DatabaseType => SupportedDatabase.MariaDb;

    // MariaDB does not have the same max_prepared_stmt_count exhaustion concern as MySQL;
    // prepared statements default ON here (MySQL defaults to OFF for safety).
    public override bool PrepareStatements => true;

    // MariaDB 10.6+ supports OFFSET/FETCH NEXT syntax; older versions use LIMIT/OFFSET only.
    public override bool SupportsOffsetFetch => IsInitialized && IsAtLeast(10, 6);

    // MariaDB inherits ANSI double-quote quoting from MySqlDialect (matches ANSI_QUOTES sql_mode)

    // MariaDB uses LAST_INSERT_ID() like MySQL
    public override string GetLastInsertedIdQuery()
    {
        return "SELECT LAST_INSERT_ID()";
    }

    public override bool SupportsIdentityColumns => true; // AUTO_INCREMENT

    // MariaDB does not provide a native JSON type; JSON is mapped to LONGTEXT
    public override bool SupportsJsonTypes => false;

    // CTEs and window functions were added in MariaDB 10.2 (vs MySQL 8.0)
    public override bool SupportsWindowFunctions => IsInitialized && IsAtLeast(10, 2);
    public override bool SupportsCommonTableExpressions => IsInitialized && IsAtLeast(10, 2);

    // MariaDB does not support INSERT ... AS alias for ON DUPLICATE KEY UPDATE.
    public override string? UpsertIncomingAlias => null;

    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        var name = await base.GetProductNameAsync(connection).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(name) && name!.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "MariaDB";
        }

        return name;
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql92;
        }

        // MariaDB version mapping (different from MySQL's simpler major version approach)
        // 10.2+: CTEs/window functions → ~SQL:2008
        if (version.Major > 10 || (version.Major == 10 && version.Minor >= 2))
        {
            return SqlStandardLevel.Sql2008;
        }

        // 10.0/10.1 era: improved standards vs 5.x
        if (version.Major >= 10)
        {
            return SqlStandardLevel.Sql2003;
        }

        // 5.x family
        if (version.Major >= 5)
        {
            return SqlStandardLevel.Sql99;
        }

        return SqlStandardLevel.Sql92;
    }

    public override string GetFinalSessionSettings(bool readOnly)
    {
        // 1 RTT / 1 Command Optimization: Combine sql_mode and session-persistent read-only intent.
        // MariaDB uses tx_read_only, so we MUST override the MySqlDialect implementation
        // that hardcodes transaction_read_only.
        var baseline = GetBaseSessionSettings();
        var intent = readOnly ? MariaDbReadOnlySql : MariaDbReadWriteSql;

        return baseline.TrimEnd(';') + "; " + intent;
    }

    public override string GetReadOnlySessionSettings() => MariaDbReadOnlySql;

    internal override string? GetReadOnlyTransactionResetSql() => MariaDbReadWriteSql;

    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        TryExecuteReadOnlySql(transaction, MariaDbReadOnlySql, "MariaDB");
    }

    public override ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        return TryExecuteReadOnlySqlAsync(transaction, MariaDbReadOnlySql, "MariaDB", cancellationToken);
    }

    private bool IsAtLeast(int major, int minor)
    {
        var v = ProductInfo.ParsedVersion;
        if (v is null)
        {
            return false;
        }

        return v.Major > major || (v.Major == major && v.Minor >= minor);
    }

    // Connection pooling properties for MariaDB
    // SupportsExternalPooling, PoolingSettingName inherited from MySqlDialect -> base (true, "Pooling")
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";
}