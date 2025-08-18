using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// SQL Server dialect with version-specific feature support
/// </summary>
public class SqlServerDialect : SqlDialect
{
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

    // SQL Server uses square brackets for quoting
    public override string QuotePrefix => "[";
    public override string QuoteSuffix => "]";

    // SQL Server supports up to SQL:2016 features in recent versions
    public override SqlStandardLevel MaxSupportedStandard => SqlStandardLevel.Sql2016;

    // Version-specific overrides
    public override bool SupportsMerge => true;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 13;

    public override string GetVersionQuery() => "SELECT @@VERSION";

    public override string GetConnectionSessionSettings()
    {
        return @"SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT OFF;";
    }

    public override void ApplyConnectionSettings(IDbConnection connection)
    {
        try
        {
            var settingsToApply = CheckSqlServerSettings(connection);

            if (!string.IsNullOrEmpty(settingsToApply))
            {
                Logger.LogDebug("Applying SQL Server session settings: {Settings}", settingsToApply);
                using var cmd = connection.CreateCommand();
                cmd.CommandText = settingsToApply;
                cmd.ExecuteNonQuery();
            }
            else
            {
                Logger.LogDebug("SQL Server session settings are already optimal, no changes needed");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to apply SQL Server connection settings");
        }
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
                var settingName = reader.GetString(0).ToUpperInvariant();
                var settingValue = reader.GetString(1);

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
            return GetConnectionSessionSettings();
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
