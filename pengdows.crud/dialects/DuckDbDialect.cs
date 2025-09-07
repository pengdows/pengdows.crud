using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// DuckDB dialect with modern analytical SQL features and excellent standard compliance
/// </summary>
public class DuckDbDialect : SqlDialect
{
    public DuckDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.DuckDB;
    public override string ParameterMarker => "$";
    public override bool SupportsNamedParameters => true;
    public override int MaxParameterLimit => 65535;
    public override int ParameterNameMaxLength => 255;
    
    // DuckDB supports prepare for modest performance gains
    public override bool PrepareStatements => true;

    // DuckDB has excellent SQL standard compliance and modern features
    public override bool SupportsMerge => false; // DuckDB doesn't support MERGE yet
    public override bool SupportsInsertOnConflict => true; // ON CONFLICT support
    public override bool SupportsJsonTypes => true; // Excellent JSON support
    public override bool SupportsArrayTypes => true; // Strong array support
    public override bool SupportsWindowFunctions => true; // Comprehensive window functions
    public override bool SupportsCommonTableExpressions => true; // Full CTE support
    public override bool SupportsNamespaces => true; // Schema support
    public override bool SupportsXmlTypes => false; // No XML support
    public override bool SupportsTemporalData => false; // No temporal tables yet
    public override bool SupportsRowPatternMatching => false; // Not yet supported
    public override bool SupportsMultidimensionalArrays => true; // Nested structures

    public override string GetVersionQuery() => "SELECT version()";

    public override void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        var cs = context.ConnectionString;
        if (readOnly && !IsMemoryConnection(cs))
        {
            cs = $"{cs};access_mode=READ_ONLY";
        }

        connection.ConnectionString = cs;
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        return readOnly ? "PRAGMA read_only = 1;" : string.Empty;
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return string.Empty;
    }

    private static bool IsMemoryConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase);
    }
    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        // Try SELECT version() first
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT version()";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is string s && !string.IsNullOrEmpty(s))
            {
                if (s.ToLowerInvariant().Contains("duckdb"))
                {
                    return "DuckDB";
                }
                // If version query succeeded but doesn't mention DuckDB, try pragma next
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to get DuckDB product name from version query");
        }

        // Fallback: try to detect DuckDB through pragma regardless of the first attempt's outcome
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "PRAGMA version";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is string s && !string.IsNullOrEmpty(s))
            {
                return "DuckDB";
            }
        }
        catch
        {
            // Ignore; if pragma also fails, return null
        }

        return null;
    }

    public override string ExtractProductNameFromVersion(string versionString)
    {
        return "DuckDB";
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            // DuckDB has excellent SQL standard compliance even in early versions
            return SqlStandardLevel.Sql2016;
        }

        // DuckDB version mapping (major.minor.patch)
        return version.Major switch
        {
            >= 1 => SqlStandardLevel.Sql2016, // v1.0+ has excellent SQL:2016 compliance
            0 when version.Minor >= 9 => SqlStandardLevel.Sql2016, // v0.9+ modern features
            0 when version.Minor >= 7 => SqlStandardLevel.Sql2011, // v0.7+ good compliance
            0 when version.Minor >= 5 => SqlStandardLevel.Sql2008, // v0.5+ basic modern features
            _ => SqlStandardLevel.Sql2003 // Early versions
        };
    }

    public override Version? ParseVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        // Try standard version parsing first
        var standardVersion = base.ParseVersion(versionString);
        if (standardVersion != null)
        {
            return standardVersion;
        }

        // DuckDB specific parsing for format like "v1.0.0" or "DuckDB v0.9.2"
        var duckDbMatch = System.Text.RegularExpressions.Regex.Match(
            versionString, 
            @"(?:DuckDB\s+)?v?(\d+)\.(\d+)\.(\d+)(?:-\w+)?", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
        if (duckDbMatch.Success)
        {
            if (int.TryParse(duckDbMatch.Groups[1].Value, out var major) &&
                int.TryParse(duckDbMatch.Groups[2].Value, out var minor) &&
                int.TryParse(duckDbMatch.Groups[3].Value, out var patch))
            {
                return new Version(major, minor, patch);
            }
        }

        return null;
    }

    public override async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        bool selectFailed = false;
        bool pragmaFailed = false;

        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT version()";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
            // If SELECT returned null/empty, tests expect an empty string, not a pragma fallback
            return string.Empty;
        }
        catch (Exception ex)
        {
            selectFailed = true;
            Logger.LogDebug(ex, "Failed to get DuckDB version using SELECT version()");
        }

        // Fallback to pragma only when SELECT failed with an exception
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "PRAGMA version";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
        }
        catch (Exception ex)
        {
            pragmaFailed = true;
            Logger.LogDebug(ex, "Failed to get DuckDB version using PRAGMA version");
        }

        // If both attempts threw errors or produced no meaningful string, return empty string to signal unknown
        if (selectFailed && pragmaFailed)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        var parameter = base.CreateDbParameter(name, type, value);
        
        // DuckDB specific parameter handling
        if (type == DbType.Guid && value is Guid guidValue)
        {
            // DuckDB handles GUIDs as strings in UUID format
            parameter.DbType = DbType.String;
            parameter.Value = guidValue.ToString();
        }
        else if (type == DbType.Boolean && value is bool boolValue)
        {
            // DuckDB has native boolean support
            parameter.DbType = DbType.Boolean;
            parameter.Value = boolValue;
        }

        return parameter;
    }

    public override bool SupportsRegularExpressions => true;
    public override bool SupportsSubqueries => true;
    public override bool SupportsOuterJoins => true;
    public override bool SupportsUnion => true;
}
