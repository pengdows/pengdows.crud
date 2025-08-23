using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// MySQL dialect with limited standard compliance
/// </summary>
public class MySqlDialect : SqlDialect
{
    public MySqlDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.MySql;
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    public override int MaxParameterLimit => 65535;
    public override int ParameterNameMaxLength => 64;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;

    public override string QuotePrefix => "`";
    public override string QuoteSuffix => "`";

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => false;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 5;
    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;

    public override string GetVersionQuery() => "SELECT VERSION()";

    public override string GetConnectionSessionSettings()
    {
      return "SET SESSION sql_mode = STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
    }

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql92;
        }

        return version.Major switch
        {
            >= 8 => SqlStandardLevel.Sql2008,
            >= 6 => SqlStandardLevel.Sql2003,
            >= 5 => SqlStandardLevel.Sql99,
            _ => SqlStandardLevel.Sql92
        };
    }
}
