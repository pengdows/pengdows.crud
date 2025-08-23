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

    public override SupportedDatabase DatabaseType => SupportedDatabase.DuckDb;
    public override string ParameterMarker => "$";
    public override bool SupportsNamedParameters => true;
    public override int MaxParameterLimit => 65535;
    public override int ParameterNameMaxLength => 255;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";

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
    public override bool SupportsEnhancedWindowFunctions => true;
    public override bool SupportsRowPatternMatching => false; // Not yet supported
    public override bool SupportsMultidimensionalArrays => true; // Nested structures
    public override bool SupportsPropertyGraphQueries => false; // Not supported

    public override string GetVersionQuery() => "SELECT version()";

    public override string GetConnectionSessionSettings()
    {
        return string.Empty;
    }
    protected override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT version()";
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                var version = reader.GetString(0);
                if (version.ToLowerInvariant().Contains("duckdb"))
                {
                    return "DuckDB";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to get DuckDB product name from version query");
        }

        // Fallback: try to detect DuckDB through pragma
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "PRAGMA version";
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return "DuckDB";
            }
        }
        catch
        {
            // Ignore, this is just a fallback
        }

        return null;
    }

    protected override string ExtractProductNameFromVersion(string versionString)
    {
        return "DuckDB";
    }

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
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

    protected override async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT version()";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result != null && !string.IsNullOrEmpty(result.ToString()))
            {
                return result.ToString()!;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to get DuckDB version using SELECT version()");
        }

        // Fallback to pragma
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "PRAGMA version";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result != null && !string.IsNullOrEmpty(result.ToString()))
            {
                return result.ToString()!;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to get DuckDB version using PRAGMA version");
        }

        return await base.GetDatabaseVersionAsync(connection).ConfigureAwait(false);
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

    /// <summary>
    /// DuckDB supports advanced SQL features and analytical queries
    /// </summary>
    public override bool SupportsRegularExpressions => true;
    
    /// <summary>
    /// DuckDB has excellent subquery optimization
    /// </summary>
    public override bool SupportsSubqueries => true;
    
    /// <summary>
    /// DuckDB supports all standard JOIN types
    /// </summary>
    public override bool SupportsOuterJoins => true;
    
    /// <summary>
    /// DuckDB supports UNION and UNION ALL
    /// </summary>
    public override bool SupportsUnion => true;
}