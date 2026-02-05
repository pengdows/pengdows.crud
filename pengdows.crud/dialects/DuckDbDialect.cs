// =============================================================================
// FILE: DuckDbDialect.cs
// PURPOSE: DuckDB specific dialect implementation for analytical workloads.
//
// AI SUMMARY:
// - Supports DuckDB 0.8+ with modern analytical SQL features.
// - Key features:
//   * MERGE statement support (DuckDB 1.4+)
//   * Parameter marker: $ (dollar sign)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 65535 (theoretical limit)
//   * Excellent SQL standard compliance
// - MERGE RETURNING support in DuckDB 1.4+.
// - DuckDB MERGE does not allow table alias on UPDATE SET left side.
// - Embedded analytics database with columnar storage.
// - Handles in-memory and file-based connections.
// - Connection mode similar to SQLite handling.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// DuckDB dialect with modern analytical SQL features.
/// </summary>
/// <remarks>
/// <para>
/// Supports DuckDB 0.8 and later with excellent SQL standard compliance.
/// Optimized for analytical/OLAP workloads with columnar storage.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement (DuckDB 1.4+).
/// Note: MERGE UPDATE SET cannot use table alias on left side.
/// </para>
/// <para>
/// <strong>Connection Modes:</strong> Similar to SQLite, uses SingleConnection
/// or SingleWriter mode based on connection string.
/// </para>
/// </remarks>
internal class DuckDbDialect : SqlDialect
{
    internal DuckDbDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.DuckDB;
    public override string ParameterMarker => "$";

    public override bool SupportsNamedParameters => true;

    // IMMUTABLE: DuckDB practical parameter limit - do not change without extensive testing
    public override int MaxParameterLimit => 65535;

    // IMMUTABLE: DuckDB identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 255;

    // DuckDB supports prepare for modest performance gains
    public override bool PrepareStatements => true;

    // DuckDB has excellent SQL standard compliance and modern features
    public override bool SupportsMerge => IsVersionAtLeast(1, 4); // MERGE support added in v1.4.0
    public override bool SupportsMergeReturning => IsVersionAtLeast(1, 4); // MERGE RETURNING support added in v1.4.0

    // DuckDB MERGE does NOT allow table alias on left side of UPDATE SET
    // Correct: UPDATE SET col = value
    // Error:   UPDATE SET t.col = value  -- "SET columns cannot be qualified"
    public override bool MergeUpdateRequiresTargetAlias => false;

    public override bool SupportsInsertOnConflict => true; // ON CONFLICT support
    public override bool SupportsJsonTypes => true; // Excellent JSON support
    public override bool SupportsArrayTypes => true; // Strong array support
    public override bool SupportsWindowFunctions => true; // Comprehensive window functions
    public override bool SupportsEnhancedWindowFunctions => true; // Advanced window functions including FILL
    public override bool SupportsCommonTableExpressions => true; // Full CTE support
    public override bool SupportsNamespaces => true; // Schema support
    public override bool SupportsXmlTypes => false; // No XML support
    public override bool SupportsTemporalData => false; // No temporal tables yet
    public override bool SupportsRowPatternMatching => false; // Not yet supported
    public override bool SupportsMultidimensionalArrays => true; // Nested structures
    public override bool SupportsInsertReturning => true; // DuckDB supports RETURNING clause
    public override bool SupportsSavepoints => false; // Skip savepoint support until DuckDB driver reliably allows it

    // Database encryption support (DuckDB 1.4.0+)
    public virtual bool SupportsEncryption => IsVersionAtLeast(1, 4); // AES-256-GCM encryption with ATTACH

    // FILL window function support (DuckDB 1.4.0+)
    public virtual bool SupportsFillWindowFunction =>
        IsVersionAtLeast(1, 4); // FILL() window function for interpolation

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"RETURNING {WrapObjectName(idColumnName)}";
    }

    public override string GetLastInsertedIdQuery()
    {
        return "SELECT lastval()"; // DuckDB supports lastval() like PostgreSQL
    }

    public override string UpsertIncomingColumn(string columnName)
    {
        return $"EXCLUDED.{WrapObjectName(columnName)}";
    }

    public override string GetVersionQuery()
    {
        return "SELECT version()";
    }

    internal override void ApplyConnectionSettingsCore(
        IDbConnection connection,
        IDatabaseContext context,
        bool readOnly,
        string? connectionStringOverride)
    {
        var cs = string.IsNullOrWhiteSpace(connectionStringOverride)
            ? context.ConnectionString
            : connectionStringOverride;

        if (readOnly && !IsMemoryConnection(cs) &&
            cs.IndexOf("access_mode=READ_ONLY", StringComparison.OrdinalIgnoreCase) < 0)
        {
            cs = $"{cs};access_mode=READ_ONLY";
        }

        // DuckDB 1.4.0+ supports database encryption via ATTACH with ENCRYPTION_KEY
        // This is handled at the SQL level via ATTACH commands, not connection string parameters
        // The connection string itself does not need modification for encryption

        connection.ConnectionString = cs;
    }

    internal override string GetReadOnlyConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return IsMemoryConnection(connectionString)
            ? connectionString
            : $"{connectionString};access_mode=READ_ONLY";
    }

    private const string ReadOnlyConnectionParam = "access_mode=READ_ONLY";
    private const string ReadOnlyPragma = "PRAGMA read_only = 1;";

    public override string? GetReadOnlyConnectionParameter()
    {
        return ReadOnlyConnectionParam;
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        return readOnly ? ReadOnlyPragma : string.Empty;
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
        var duckDbMatch = Regex.Match(
            versionString,
            @"(?:DuckDB\s+)?v?(\d+)\.(\d+)\.(\d+)(?:-\w+)?",
            RegexOptions.IgnoreCase);

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
        var selectFailed = false;
        var pragmaFailed = false;

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

    // Connection pooling properties for DuckDB (in-process, no pooling)
    public override bool SupportsExternalPooling => false; // in-process
    public override string? PoolingSettingName => null;
    public override string? MinPoolSizeSettingName => null;
    public override string? MaxPoolSizeSettingName => null;
    internal override int DefaultMaxPoolSize => int.MaxValue;
}
