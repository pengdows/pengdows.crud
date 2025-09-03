using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// PostgreSQL dialect with comprehensive standard support
/// </summary>
public class PostgreSqlDialect : SqlDialect
{
    public PostgreSqlDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.PostgreSql;
    public override string ParameterMarker => ":";
    public override bool SupportsNamedParameters => true;
    public override int MaxParameterLimit => 32767;
    public override int MaxOutputParameters => 100;
    public override int ParameterNameMaxLength => 63;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.PostgreSQL;
    public override bool RequiresStoredProcParameterNameMatch => true;
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => IsInitialized && ProductInfo.ParsedVersion?.Major >= 15;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 9;
    public override bool SupportsSqlJsonConstructors => IsInitialized && ProductInfo.ParsedVersion?.Major >= 18;
    public override bool SupportsJsonTable => IsInitialized && ProductInfo.ParsedVersion?.Major >= 18;
    public override bool SupportsMergeReturning => IsInitialized && ProductInfo.ParsedVersion?.Major >= 18;

    public override string GetVersionQuery() => "SELECT version()";

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        var baseSettings = GetConnectionSessionSettings();
        if (readOnly)
        {
            return $"{baseSettings}\nSET default_transaction_read_only = on;";
        }

        return baseSettings;
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return @"SET standard_conforming_strings = on;
SET client_min_messages = warning;
SET search_path = public;";
    }

    public override void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        var cs = context.ConnectionString;
        if (readOnly)
        {
            cs = $"{cs};Options='-c default_transaction_read_only=on'";
        }

        connection.ConnectionString = cs;
    }

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql2008;
        }

        return version.Major switch
        {
            >= 15 => SqlStandardLevel.Sql2016,
            >= 13 => SqlStandardLevel.Sql2011,
            >= 11 => SqlStandardLevel.Sql2008,
            >= 9 => SqlStandardLevel.Sql2003,
            _ => SqlStandardLevel.Sql92
        };
    }
}
