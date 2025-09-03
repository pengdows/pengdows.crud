using System.Data;
using System.Data.Common;
using System.Reflection;
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
    public override int MaxParameterLimit => 64000;
    public override int MaxOutputParameters => 1024;
    public override int ParameterNameMaxLength => 30;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Oracle;
    public override bool RequiresStoredProcParameterNameMatch => true;
    
    // Oracle prefers statement cache and array binding over manual prepare
    public override bool PrepareStatements => false;
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    public override bool SupportsMerge => true;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 12;

    public override string GetVersionQuery() => "SELECT * FROM v$version WHERE banner LIKE 'Oracle%'";

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        var baseSettings = GetConnectionSessionSettings();
        if (readOnly)
        {
            return $"{baseSettings}\nALTER SESSION SET READ ONLY;";
        }

        return baseSettings;
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';";
    }

    public override void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        // Configure Oracle-specific connection settings for optimal performance
        if (connection.GetType().FullName?.Contains("Oracle") == true)
        {
            try
            {
                // Set StatementCacheSize for better performance with repeated queries
                var connectionType = connection.GetType();
                var statementCacheSizeProperty = connectionType.GetProperty("StatementCacheSize");
                if (statementCacheSizeProperty != null)
                {
                    var currentCacheSize = statementCacheSizeProperty.GetValue(connection);
                    if (currentCacheSize is int size && size < 64)
                    {
                        statementCacheSizeProperty.SetValue(connection, 64);
                    }
                }
                
                Logger.LogDebug("Applied Oracle connection settings: StatementCacheSize configured");
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to configure Oracle connection settings, using defaults");
            }
        }
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
