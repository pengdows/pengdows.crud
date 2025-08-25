using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// SQL Server dialect with version-specific feature support
/// </summary>
public class SqlServerDialect : SqlDialect
{
    private const string DefaultSessionSettings =
        "SET NOCOUNT ON;\n" +
        "SET ANSI_NULLS ON;\n" +
        "SET ANSI_PADDING ON;\n" +
        "SET ANSI_WARNINGS ON;\n" +
        "SET ARITHABORT ON;\n" +
        "SET CONCAT_NULL_YIELDS_NULL ON;\n" +
        "SET QUOTED_IDENTIFIER ON;\n" +
        "SET NUMERIC_ROUNDABORT OFF;\n" +
        "SET NOCOUNT OFF;";

    private string? _sessionSettings;

    public SqlServerDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    public override int MaxParameterLimit => 2100;
    public override int ParameterNameMaxLength => 128;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Exec;
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";
    public override bool SupportsNamespaces => true;

    // Version-specific overrides
    public override bool SupportsMerge => true;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 13;

    public override string GetVersionQuery() => "SELECT @@VERSION";

    public override string GetConnectionSessionSettings()
    {
        return _sessionSettings ?? string.Empty;
    }

    public override bool IsReadCommittedSnapshotOn(ITrackedConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()";
        var val = cmd.ExecuteScalar();
        var v = val is int i ? i : Convert.ToInt32(val ?? 0);
        return v == 1;
    }

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);
        
        // Check and cache SQL Server session settings during initialization
        if (_sessionSettings == null)
        {
            _sessionSettings = CheckSqlServerSettings(connection);
        }
        
        return productInfo;
    }


    private string CheckSqlServerSettings(IDbConnection connection)
    {
        try
        {
            var expectedSettings = new Dictionary<string, string>
            {
                { "ANSI_NULLS", "ON" },
                { "ANSI_PADDING", "ON" },
                { "ANSI_WARNINGS", "ON" },
                { "ARITHABORT", "ON" },
                { "CONCAT_NULL_YIELDS_NULL", "ON" },
                { "QUOTED_IDENTIFIER", "ON" },
                { "NUMERIC_ROUNDABORT", "OFF" }
            };

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DBCC USEROPTIONS;";

            using var reader = cmd.ExecuteReader();
            var currentSettings = expectedSettings.ToDictionary(kvp => kvp.Key, _ => "OFF");

            while (reader.Read())
            {
                string settingName;
                string settingValue;

                if (reader.FieldCount == 1)
                {
                    settingName = reader.GetName(0).ToUpperInvariant();
                    settingValue = reader.GetString(0);
                }
                else
                {
                    settingName = reader.GetString(0).ToUpperInvariant();
                    settingValue = reader.GetString(1);
                }

                if (expectedSettings.ContainsKey(settingName))
                {
                    currentSettings[settingName] = settingValue == "SET" ? "ON" : "OFF";
                }
            }

            var sb = new StringBuilder();
            foreach (var expectedSetting in expectedSettings)
            {
                var settingName = expectedSetting.Key;
                var expectedValue = expectedSetting.Value;

                currentSettings.TryGetValue(settingName, out var currentValue);

                if (currentValue != expectedValue)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append($"SET {settingName} {expectedValue}");
                }
            }

            if (sb.Length > 0)
            {
                sb.Insert(0, "SET NOCOUNT ON;\n");
                sb.AppendLine(";\nSET NOCOUNT OFF;");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to check SQL Server session settings, applying default settings");
            return DefaultSessionSettings;
        }
    }

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql2008;
        }

        return version.Major switch
        {
            >= 16 => SqlStandardLevel.Sql2016,
            >= 15 => SqlStandardLevel.Sql2016,
            >= 14 => SqlStandardLevel.Sql2016,
            >= 13 => SqlStandardLevel.Sql2016,
            >= 12 => SqlStandardLevel.Sql2011,
            >= 11 => SqlStandardLevel.Sql2008,
            >= 10 => SqlStandardLevel.Sql2008,
            _ => SqlStandardLevel.Sql2003
        };
    }
}
