using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// MariaDB dialect. Syntax is largely MySQL-compatible but feature availability
/// (e.g., CTEs, window functions, JSON types) differs by version and from MySQL.
/// </summary>
public class MariaDbDialect : SqlDialect
{
    private const string DesiredSqlMode = "ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_ZERO_DATE,NO_ZERO_IN_DATE,NO_ENGINE_SUBSTITUTION";
    private const string DesiredTimeZone = "+00:00";
    private const string DesiredCharset = "utf8mb4";
    private const string DesiredCollation = "utf8mb4_general_ci";
    private const string DesiredIsolation = "READ COMMITTED";
    private const string DesiredGroupConcatMaxLen = "1048576";
    private string? _sessionSettings;
    public MariaDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.MariaDb;
    public override string QuotePrefix => "`";
    public override string QuoteSuffix => "`";
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    // Limits reflect MariaDB/MySQL protocol and server behavior.
    // - Max parameters: 65,535 per statement (16-bit unsigned)
    // - Max output parameters: same order of magnitude; safe cap at 65,535
    // - Parameter name length: 64 characters
    // Adjust only if upstream server/protocol constraints change.
    public override int MaxParameterLimit => 65535;
    public override int MaxOutputParameters => 65535;
    public override int ParameterNameMaxLength => 64;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;
    
    // MariaDB benefits from server-side prepared statements like MySQL
    public override bool PrepareStatements => true;

    public override bool SupportsNamespaces => true;

    // MariaDB supports ON DUPLICATE KEY like MySQL
    public override bool SupportsOnDuplicateKey => true;
    public override bool SupportsMerge => false;
    public override bool SupportsIdentityColumns => true; // AUTO_INCREMENT

    // MariaDB does not provide a native JSON type; JSON is mapped to LONGTEXT
    public override bool SupportsJsonTypes => false;

    // CTEs and window functions were added in MariaDB 10.2.
    // Reflect real behavior: return true only when initialized and version >= 10.2
    // Tests that expect "modern" behavior should initialize the dialect first.
    public override bool SupportsWindowFunctions => IsInitialized && IsAtLeast(10, 2);
    public override bool SupportsCommonTableExpressions => IsInitialized && IsAtLeast(10, 2);

    public override string GetVersionQuery() => "SELECT VERSION()";

    public override string GetLastInsertedIdQuery()
    {
        // MariaDB uses the same LAST_INSERT_ID() function as MySQL
        return "SELECT LAST_INSERT_ID()";
    }

    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        var name = await base.GetProductNameAsync(connection).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(name) && name!.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "MariaDB";
        }
        return name;
    }

    public override string GetBaseSessionSettings()
    {
        return _sessionSettings ?? string.Empty;
    }

    public override string GetReadOnlySessionSettings()
    {
        return "SET SESSION TRANSACTION READ ONLY;";
    }

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var info = await base.DetectDatabaseInfoAsync(connection).ConfigureAwait(false);
        if (_sessionSettings == null)
        {
            _sessionSettings = CheckSessionSettings(connection);
            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying MariaDB session settings on first connect:\n{Settings}", _sessionSettings);
            }
            else
            {
                Logger.LogInformation("MariaDB session settings: no changes required (already compliant)");
            }
        }

        return info;
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
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

    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        try
        {
            using var sc = transaction.CreateSqlContainer("SET SESSION TRANSACTION READ ONLY;");
            sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to apply MariaDB read-only session settings");
        }
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

    private string CheckSessionSettings(IDbConnection connection)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SHOW VARIABLES WHERE Variable_name IN ('sql_mode','time_zone','character_set_client','collation_connection','transaction_isolation','tx_isolation','sql_notes','innodb_strict_mode','sql_safe_updates','sql_auto_is_null','group_concat_max_len')";
            using var reader = cmd.ExecuteReader();
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var value = reader.GetString(1);
                current[name] = value;
            }

            var sets = new List<string>();

            if (!SqlModeEquals(current.TryGetValue("sql_mode", out var sqlMode) ? sqlMode : null, DesiredSqlMode))
            {
                sets.Add($"SET SESSION sql_mode = '{DesiredSqlMode}'");
            }

            if (!string.Equals(current.GetValueOrDefault("time_zone"), DesiredTimeZone, StringComparison.OrdinalIgnoreCase))
            {
                sets.Add($"SET time_zone = '{DesiredTimeZone}'");
            }

            if (!string.Equals(current.GetValueOrDefault("character_set_client"), DesiredCharset, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.GetValueOrDefault("collation_connection"), DesiredCollation, StringComparison.OrdinalIgnoreCase))
            {
                sets.Add($"SET NAMES {DesiredCharset} COLLATE {DesiredCollation}");
            }

            var isolation = current.GetValueOrDefault("transaction_isolation") ?? current.GetValueOrDefault("tx_isolation");
            if (!IsolationEquals(isolation, DesiredIsolation))
            {
                sets.Add($"SET SESSION TRANSACTION ISOLATION LEVEL {DesiredIsolation}");
            }

            if (!string.Equals(current.GetValueOrDefault("sql_notes"), "0", StringComparison.OrdinalIgnoreCase))
            {
                sets.Add("SET SESSION sql_notes = 0");
            }

            if (!string.Equals(current.GetValueOrDefault("innodb_strict_mode"), "ON", StringComparison.OrdinalIgnoreCase))
            {
                sets.Add("SET SESSION innodb_strict_mode = ON");
            }

            if (!string.Equals(current.GetValueOrDefault("sql_safe_updates"), "0", StringComparison.OrdinalIgnoreCase))
            {
                sets.Add("SET SESSION sql_safe_updates = 0");
            }

            if (!string.Equals(current.GetValueOrDefault("sql_auto_is_null"), "0", StringComparison.OrdinalIgnoreCase))
            {
                sets.Add("SET SESSION sql_auto_is_null = 0");
            }

            if (!string.Equals(current.GetValueOrDefault("group_concat_max_len"), DesiredGroupConcatMaxLen, StringComparison.OrdinalIgnoreCase))
            {
                sets.Add($"SET SESSION group_concat_max_len = {DesiredGroupConcatMaxLen}");
            }

            return sets.Count == 0 ? string.Empty : string.Join(";\n", sets) + ";";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to check MariaDB session settings, applying defaults");
            return string.Join(";\n", new[]
            {
                $"SET SESSION sql_mode = '{DesiredSqlMode}'",
                $"SET time_zone = '{DesiredTimeZone}'",
                $"SET NAMES {DesiredCharset} COLLATE {DesiredCollation}",
                $"SET SESSION TRANSACTION ISOLATION LEVEL {DesiredIsolation}",
                "SET SESSION sql_notes = 0",
                "SET SESSION innodb_strict_mode = ON",
                "SET SESSION sql_safe_updates = 0",
                "SET SESSION sql_auto_is_null = 0",
                $"SET SESSION group_concat_max_len = {DesiredGroupConcatMaxLen}"
            }) + ";";
        }
    }

    private static bool SqlModeEquals(string? current, string expected)
    {
        if (current is null)
        {
            return false;
        }

        var expectedSet = new HashSet<string>(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var currentSet = new HashSet<string>(current.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        return expectedSet.SetEquals(currentSet);
    }

    private static bool IsolationEquals(string? current, string expected)
    {
        if (current is null)
        {
            return false;
        }

        var normalized = current.Replace('-', ' ').Trim().ToUpperInvariant();
        var desired = expected.Replace('-', ' ').Trim().ToUpperInvariant();
        return normalized == desired;
    }
}
