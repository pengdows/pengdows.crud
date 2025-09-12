using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Firebird dialect with SQL standard compliance and database-specific features
/// </summary>
public class FirebirdDialect : SqlDialect
{
    public FirebirdDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Firebird;
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    public override bool SupportsSavepoints => true;
    // IMMUTABLE: Firebird theoretical parameter limit - do not change without extensive testing
    public override int MaxParameterLimit => 65535;
    // IMMUTABLE: Firebird PSQL practical output parameter limit - do not change without extensive testing
    public override int MaxOutputParameters => 1499;
    // IMMUTABLE: Firebird identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 63;
    
    // Firebird benefits from prepared statements
    public override bool PrepareStatements => true;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.ExecuteProcedure;

    public override bool SupportsMerge => IsInitialized && ProductInfo.ParsedVersion?.Major >= 2;
    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 3;
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 2;
    public override bool SupportsJsonTypes => false;
    public override bool SupportsArrayTypes => true;

    public override string GetVersionQuery() => "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database";

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        // Firebird doesn't have separate read-only session settings like other databases
        return "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;\nSET SQL DIALECT 3;";
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;\nSET SQL DIALECT 3;";
    }


    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        // Prefer scalar results to match fakeDb test helpers
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result != null)
            {
                return "Firebird";
            }
        }
        catch
        {
            try
            {
                await using var cmd = (DbCommand)connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM rdb$database";
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                if (result != null)
                {
                    return "Firebird";
                }
            }
            catch
            {
                // ignored
            }
        }

        return null;
    }

    public override string ExtractProductNameFromVersion(string versionString)
    {
        return "Firebird";
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql92;
        }

        return version.Major switch
        {
            >= 5 => SqlStandardLevel.Sql2016,
            >= 4 => SqlStandardLevel.Sql2011,
            >= 3 => SqlStandardLevel.Sql2008,
            >= 2 => SqlStandardLevel.Sql2003,
            _ => SqlStandardLevel.Sql92
        };
    }

    public override Version? ParseVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        var standardVersion = base.ParseVersion(versionString);
        if (standardVersion != null)
        {
            return standardVersion;
        }

        var legacyMatch = System.Text.RegularExpressions.Regex.Match(versionString, @"LI-V(\d+)\.(\d+)\.(\d+)");
        if (legacyMatch.Success)
        {
            if (int.TryParse(legacyMatch.Groups[1].Value, out var major) &&
                int.TryParse(legacyMatch.Groups[2].Value, out var minor) &&
                int.TryParse(legacyMatch.Groups[3].Value, out var build))
            {
                return new Version(major, minor, build);
            }
        }

        var firebirdMatch = System.Text.RegularExpressions.Regex.Match(versionString, @"Firebird\s+(\d+)\.(\d+)");
        if (firebirdMatch.Success)
        {
            if (int.TryParse(firebirdMatch.Groups[1].Value, out var major) &&
                int.TryParse(firebirdMatch.Groups[2].Value, out var minor))
            {
                return new Version(major, minor);
            }
        }

        return null;
    }

    public override async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        // Try engine context first; if returns null or empty, surface empty (do not attempt monitor)
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            var s = result?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(s))
            {
                return s;
            }
            // If engine query returned null/empty, tests expect an empty string, not a monitor fallback
            return string.Empty;
        }
        catch
        {
            // ignore and try monitor table next
        }

        // Try monitor table; same null/empty handling
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT mon$server_version FROM mon$database";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            var s = result?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(s))
            {
                return s;
            }
        }
        catch
        {
            // ignore and fall through
        }

        // Both attempts failed (engine threw and monitor had no data)
        return string.Empty;
    }

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        var parameter = base.CreateDbParameter(name, type, value);
        if (type == DbType.Boolean)
        {
            parameter.DbType = DbType.Int16;
            if (value is bool boolValue)
            {
                parameter.Value = boolValue ? (short)1 : (short)0;
            }
        }
        else if (type == DbType.Guid)
        {
            parameter.DbType = DbType.Binary;
            if (value is Guid guidValue)
            {
                parameter.Value = guidValue.ToByteArray();
            }
        }

        return parameter;
    }
}
