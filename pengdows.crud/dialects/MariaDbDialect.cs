using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

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
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    public override int MaxParameterLimit => 65535;
    public override int MaxOutputParameters => 65535;
    public override int ParameterNameMaxLength => 64;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;

    public override bool SupportsNamespaces => true;

    // MariaDB supports ON DUPLICATE KEY like MySQL
    public override bool SupportsOnDuplicateKey => true;
    public override bool SupportsMerge => false;

    // MariaDB does not provide a native JSON type; JSON is mapped to LONGTEXT
    public override bool SupportsJsonTypes => false;

    // CTEs and window functions were added in MariaDB 10.2 (approximate gate)
    public override bool SupportsWindowFunctions => IsInitialized && IsAtLeast(10, 2);
    public override bool SupportsCommonTableExpressions => IsInitialized && IsAtLeast(10, 2);

    public override string GetVersionQuery() => "SELECT VERSION()";

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        var baseSettings = GetConnectionSessionSettings();
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

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
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
