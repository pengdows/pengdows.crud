// =============================================================================
// FILE: MariaDbDialect.cs
// PURPOSE: MariaDB specific dialect implementation (extends MySqlDialect).
//
// AI SUMMARY:
// - Inherits from MySqlDialect with MariaDB-specific overrides.
// - Key differences from MySQL:
//   * No native JSON type (uses LONGTEXT)
//   * CTEs and window functions in 10.2+ (earlier than MySQL 8.0)
//   * No INSERT ... AS alias syntax for upserts
// - Uses LAST_INSERT_ID() for returning generated IDs.
// - Session settings: Inherits ANSI_QUOTES mode from MySqlDialect.
// - AUTO_INCREMENT for identity columns.
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
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
/// <item><description>No native JSON type (mapped to LONGTEXT)</description></item>
/// <item><description>CTEs and window functions available in 10.2+ (vs MySQL 8.0)</description></item>
/// <item><description>Different upsert alias syntax handling</description></item>
/// </list>
/// </remarks>
internal class MariaDbDialect : MySqlDialect
{
    internal MariaDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    // Only override what's different from MySQL
    public override SupportedDatabase DatabaseType => SupportedDatabase.MariaDb;

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

    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        try
        {
            using var sc = transaction.CreateSqlContainer("SET SESSION TRANSACTION READ ONLY;");
            sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to apply MariaDB read-only session settings");
        }
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
    public override bool SupportsExternalPooling => true;
    public override string? PoolingSettingName => "Pooling";
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";
    public override string? ApplicationNameSettingName => "Application Name";
}