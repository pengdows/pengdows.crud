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
        // Minimal, individual SET statements; no NOCOUNT wrappers; semicolons used only as delimiters.
        "SET ANSI_NULLS ON;\n" +
        "SET ANSI_PADDING ON;\n" +
        "SET ANSI_WARNINGS ON;\n" +
        "SET ARITHABORT ON;\n" +
        "SET CONCAT_NULL_YIELDS_NULL ON;\n" +
        "SET QUOTED_IDENTIFIER ON;\n" +
        "SET NUMERIC_ROUNDABORT OFF;";

    private string? _sessionSettings;

    internal SqlServerDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    // IMMUTABLE: SQL Server sp_executesql documented limit - do not change without extensive testing
    public override int MaxParameterLimit => 2100;
    // IMMUTABLE: SQL Server output parameter limit - do not change without extensive testing  
    public override int MaxOutputParameters => 1024;
    // IMMUTABLE: SQL Server identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 128;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Exec;
    
    // SQL Server relies on sp_executesql and server plan cache, not manual prepare
    public override bool PrepareStatements => false;
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    // Version-specific overrides
    public override bool SupportsMerge => IsVersionAtLeast(10);
    public override bool SupportsJsonTypes => IsVersionAtLeast(13);

    public override bool SupportsInsertReturning => true;
    public override bool SupportsIdentityColumns => true;

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"OUTPUT INSERTED.{WrapObjectName(idColumnName)}";
    }

    public override string GetLastInsertedIdQuery()
    {
        // Fallback method - prefer OUTPUT clause
        return "SELECT SCOPE_IDENTITY()";
    }

    public override string GetVersionQuery() => "SELECT @@VERSION";

    public override async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = GetVersionQuery();
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is null)
            {
                return string.Empty;
            }
            return result.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            // For direct version queries, surface error context as string (legacy behavior expected by tests)
            return $"Error retrieving version: {ex.Message}";
        }
    }

    public override string GetBaseSessionSettings()
    {
        // SQL Server doesn't differentiate session settings based on read-only mode
        // as it uses ApplicationIntent=ReadOnly in the connection string instead
        return _sessionSettings ?? string.Empty;
    }

    public override string? GetReadOnlyConnectionParameter()
    {
        return "ApplicationIntent=ReadOnly";
    }

    [Obsolete]
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

    // SQL Server uses base class ApplyConnectionSettings implementation

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);
        
        // Check and cache SQL Server session settings during initialization
        if (_sessionSettings == null)
        {
            _sessionSettings = CheckSqlServerSettings(connection);
            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying SQL Server session settings on first connect:\n{Settings}", _sessionSettings);
            }
            else
            {
                Logger.LogInformation("SQL Server session settings: no changes required (already compliant)");
            }
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
            cmd.CommandText = "DBCC USEROPTIONS"; // no trailing ';'

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
                    // Return individual SET statements; execution loop will split/execute one-by-one
                    sb.Append($"SET {settingName} {expectedValue};");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to check SQL Server session settings, applying default settings");
            // Provide minimal, individual statements (no NOCOUNT wrapper, no multi-batch)
            return DefaultSessionSettings;
        }
    }

    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>
        {
            { 13, SqlStandardLevel.Sql2016 }, // SQL Server 2016+
            { 12, SqlStandardLevel.Sql2011 }, // SQL Server 2014
            { 10, SqlStandardLevel.Sql2008 }, // SQL Server 2008+
            { 8,  SqlStandardLevel.Sql2003 }  // SQL Server 2000+
        };
    }

    public override SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql2008;
    }

    // Connection pooling properties for SQL Server
    public override bool SupportsExternalPooling => true;
    public override string? PoolingSettingName => "Pooling";
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";
}
