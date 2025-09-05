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
    // Use ':' parameter marker; Npgsql supports ':' and existing integrations rely on it
    public override string ParameterMarker => ":";
    public override bool SupportsNamedParameters => true;
    public override bool SupportsSetValuedParameters => true;
    public override int MaxParameterLimit => 32767;
    public override int MaxOutputParameters => 100;
    public override int ParameterNameMaxLength => 63;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.PostgreSQL;
    public override bool RequiresStoredProcParameterNameMatch => true;
    
    // PostgreSQL benefits from prepared statements
    public override bool PrepareStatements => true;
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => IsVersionAtLeast(15);
    public override bool SupportsJsonTypes => IsVersionAtLeast(9);
    public override bool SupportsSqlJsonConstructors => IsVersionAtLeast(18);
    public override bool SupportsJsonTable => IsVersionAtLeast(18);
    public override bool SupportsMergeReturning => IsVersionAtLeast(18);

    public override string GetVersionQuery() => "SELECT version()";

    protected override string GetBaseSessionSettings()
    {
        return @"SET standard_conforming_strings = on;
SET client_min_messages = warning;
SET search_path = public;";
    }

    protected override string GetReadOnlySessionSettings()
    {
        return "SET default_transaction_read_only = on;";
    }

    protected override string? GetReadOnlyConnectionParameter()
    {
        return "Options='-c default_transaction_read_only=on'";
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return @"SET standard_conforming_strings = on;
SET client_min_messages = warning;
SET search_path = public;";
    }

    protected override void ConfigureProviderSpecificSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        // Apply Npgsql-specific prepare settings if this is an Npgsql connection
        if (connection.GetType().FullName?.StartsWith("Npgsql.") == true)
        {
            try
            {
                // Use the inherited ConnectionStringBuilder property instead of reflection
                ConnectionStringBuilder.ConnectionString = connection.ConnectionString;
                var builder = ConnectionStringBuilder;
                
                // Configure auto-prepare settings for optimal prepared statement performance
                if (builder.ContainsKey("MaxAutoPrepare") && (int)builder["MaxAutoPrepare"] == 0)
                {
                    builder["MaxAutoPrepare"] = 64;
                }
                else if (!builder.ContainsKey("MaxAutoPrepare"))
                {
                    builder["MaxAutoPrepare"] = 64;
                }
                
                if (builder.ContainsKey("AutoPrepareMinUsages") && (int)builder["AutoPrepareMinUsages"] == 0)
                {
                    builder["AutoPrepareMinUsages"] = 2;
                }
                else if (!builder.ContainsKey("AutoPrepareMinUsages"))
                {
                    builder["AutoPrepareMinUsages"] = 2;
                }
                
                // Multiplexing must be disabled for prepare to work properly
                builder["Multiplexing"] = false;
                
                connection.ConnectionString = builder.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to configure Npgsql connection string settings, using original connection string");
            }
        }
    }

    protected override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>
        {
            { 15, SqlStandardLevel.Sql2016 },
            { 13, SqlStandardLevel.Sql2011 },
            { 11, SqlStandardLevel.Sql2008 },
            { 9, SqlStandardLevel.Sql2003 },
            { 8, SqlStandardLevel.Sql92 }
        };
    }

    protected override SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql2008;
    }
}
