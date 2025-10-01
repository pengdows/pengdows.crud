using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// MariaDB dialect. Inherits MySQL compatibility with MariaDB-specific feature differences
/// (e.g., CTEs, window functions available in 10.2+, no native JSON type).
/// </summary>
public class MariaDbDialect : MySqlDialect
{
    public MariaDbDialect(DbProviderFactory factory, ILogger logger)
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
}
