using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// MariaDB dialect. Syntax is largely MySQL-compatible but feature availability
/// (e.g., CTEs, window functions, JSON types) differs by version and from MySQL.
/// </summary>
public class MariaDbDialect : SqlDialect
{
    public MariaDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.MariaDb;
    public override string QuotePrefix => "`";
    public override string QuoteSuffix => "`";
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    // Limits reflect MariaDB/MySQL protocol and server behavior.
    // - Max parameters: 65,535 per statement (16-bit unsigned)
    // - Max output parameters: same order of magnitude; safe cap at 65,535
    // - Parameter name length: 64 characters
    // Adjust only if upstream server/protocol constraints change.
    public override int MaxParameterLimit => 65535;
    public override int MaxOutputParameters => 65535;
    public override int ParameterNameMaxLength => 64;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;
    
    // MariaDB benefits from server-side prepared statements like MySQL
    public override bool PrepareStatements => true;

    public override bool SupportsNamespaces => true;

    // MariaDB supports ON DUPLICATE KEY like MySQL
    public override bool SupportsOnDuplicateKey => true;
    public override bool SupportsMerge => false;
    public override bool SupportsIdentityColumns => true; // AUTO_INCREMENT

    // MariaDB does not provide a native JSON type; JSON is mapped to LONGTEXT
    public override bool SupportsJsonTypes => false;

    // CTEs and window functions were added in MariaDB 10.2.
    // Reflect real behavior: return true only when initialized and version >= 10.2
    // Tests that expect "modern" behavior should initialize the dialect first.
    public override bool SupportsWindowFunctions => IsInitialized && IsAtLeast(10, 2);
    public override bool SupportsCommonTableExpressions => IsInitialized && IsAtLeast(10, 2);

    public override string GetVersionQuery() => "SELECT VERSION()";

    public override string GetLastInsertedIdQuery()
    {
        // MariaDB uses the same LAST_INSERT_ID() function as MySQL
        return "SELECT LAST_INSERT_ID()";
    }

    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        var name = await base.GetProductNameAsync(connection).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(name) && name!.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "MariaDB";
        }
        return name;
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        // Align with ANSI quoting and predictable behavior similar to MySQL settings
        const string baseSettings = "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
        
        if (readOnly)
        {
            return $"{baseSettings}\nSET SESSION TRANSACTION READ ONLY;";
        }

        return baseSettings;
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        // Align with ANSI quoting and predictable behavior similar to MySQL settings
        return "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql92;
        }

        // Approximate mapping based on major feature introductions
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

    public override string UpsertIncomingColumn(string columnName)
    {
        // MariaDB follows MySQL semantics for ON DUPLICATE KEY ... VALUES(col)
        return $"VALUES({WrapObjectName(columnName)})";
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
}
