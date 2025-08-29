using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// Oracle dialect with comprehensive enterprise features
/// </summary>
public class OracleDialect : SqlDialect
{
    public OracleDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Oracle;
    public override string ParameterMarker => ":";
    public override bool SupportsNamedParameters => true;
    public override int MaxParameterLimit => 1000;
    public override int MaxOutputParameters => 1024;
    public override int ParameterNameMaxLength => 30;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Oracle;
    public override bool RequiresStoredProcParameterNameMatch => true;
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    public override bool SupportsMerge => true;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 12;

    public override string GetVersionQuery() => "SELECT * FROM v$version WHERE banner LIKE 'Oracle%'";

    public override string GetConnectionSessionSettings()
    {
        return "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';";
    }

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql2003;
        }

        return version.Major switch
        {
            >= 21 => SqlStandardLevel.Sql2016,
            >= 19 => SqlStandardLevel.Sql2016,
            >= 18 => SqlStandardLevel.Sql2011,
            >= 12 => SqlStandardLevel.Sql2008,
            >= 11 => SqlStandardLevel.Sql2003,
            _ => SqlStandardLevel.Sql99
        };
    }
}
