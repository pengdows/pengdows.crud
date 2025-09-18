using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// MySQL dialect with limited standard compliance
/// </summary>
public class MySqlDialect : SqlDialect
{
    private string? _sessionSettings;
    private readonly bool _isMySqlConnector;

    public MySqlDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
        var ns = factory.GetType().Namespace ?? string.Empty;
        _isMySqlConnector = ns.Contains("MySqlConnector", StringComparison.OrdinalIgnoreCase);
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.MySql;
    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    // IMMUTABLE: MySQL theoretical maximum parameter limit - do not change without extensive testing
    public override int MaxParameterLimit => 65535;
    // IMMUTABLE: MySQL output parameter limit - do not change without extensive testing
    public override int MaxOutputParameters => 65535;
    // IMMUTABLE: MySQL identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 64;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;
    
    // MySQL benefits from server-side prepared statements
    public override bool PrepareStatements => true;

    public override bool SupportsNamespaces => true;

    public override bool SupportsOnDuplicateKey => true; // Available since MySQL 4.1 (2004) - safe to assume
    public override bool SupportsMerge => false;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 5;
    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 8;

    public override string GetVersionQuery() => "SELECT VERSION()";

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);
        
        // Check and cache MySQL session settings during initialization
        if (_sessionSettings == null)
        {
            _sessionSettings = CheckMySqlSettings(connection);
            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying MySQL session settings on first connect:\n{Settings}", _sessionSettings);
            }
            else
            {
                Logger.LogInformation("MySQL session settings: no changes required (already compliant)");
            }
        }
        
        return productInfo;
    }

    private string CheckMySqlSettings(IDbConnection connection)
    {
        try
        {
            var expectedSqlMode = "STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT @@sql_mode";

            var currentSqlMode = cmd.ExecuteScalar()?.ToString() ?? "";
            
            var sb = new StringBuilder();
            
            // Check if current sql_mode contains all expected modes
            if (!SqlModeContainsAll(currentSqlMode, expectedSqlMode))
            {
                sb.Append($"SET SESSION sql_mode = '{expectedSqlMode}';");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to check MySQL session settings, applying default settings");
            return "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
        }
    }

    private static bool SqlModeContainsAll(string currentMode, string expectedMode)
    {
        var currentModes = currentMode.Split(',').Select(m => m.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedModes = expectedMode.Split(',').Select(m => m.Trim());
        
        return expectedModes.All(expectedModes => currentModes.Contains(expectedModes));
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        const string defaultSqlMode = "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
        var baseSettings = _sessionSettings ?? defaultSqlMode;
        
        if (readOnly)
        {
            return $"{baseSettings}\nSET SESSION TRANSACTION READ ONLY;";
        }

        return baseSettings;
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        const string defaultSqlMode = "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
        return _sessionSettings ?? defaultSqlMode;
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
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

    public override string UpsertIncomingColumn(string columnName)
    {
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
            Logger.LogDebug(ex, "Failed to apply MySQL read-only session settings");
        }
    }

    // Connection pooling properties for MySQL (provider-aware)
    public override bool SupportsExternalPooling => true;
    public override string? PoolingSettingName => "Pooling";
    public override string? MinPoolSizeSettingName => _isMySqlConnector ? "MinimumPoolSize" : "Min Pool Size";
    public override string? MaxPoolSizeSettingName => _isMySqlConnector ? "MaximumPoolSize" : "Max Pool Size";
}
