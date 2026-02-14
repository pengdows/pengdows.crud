// =============================================================================
// FILE: FirebirdDialect.cs
// PURPOSE: Firebird Database specific dialect implementation.
//
// AI SUMMARY:
// - Supports Firebird 2.5+ and 3.0+ with version-specific features.
// - Key features:
//   * MERGE statement support (Firebird 2.0+)
//   * Parameter marker: @ (at sign)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 65535 (theoretical limit)
//   * EXECUTE PROCEDURE for stored proc calls
// - Window functions support in Firebird 3.0+.
// - RETURNING clause for getting generated IDs.
// - Generator (sequence) based ID generation.
// - Embedded and server modes supported.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Firebird Database dialect with SQL standard compliance.
/// </summary>
/// <remarks>
/// <para>
/// Supports Firebird 2.5 and later with automatic version detection.
/// Firebird 3.0+ adds window function support.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement (Firebird 2.0+).
/// </para>
/// <para>
/// <strong>ID Generation:</strong> Uses generators (sequences) with
/// RETURNING clause to fetch generated values.
/// </para>
/// </remarks>
internal class FirebirdDialect : SqlDialect
{
    internal FirebirdDialect(DbProviderFactory factory, ILogger logger)
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

    // Firebird provider can be overly strict during explicit prepare; defer to execution-time preparation
    public override bool PrepareStatements => false;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.ExecuteProcedure;

    public override bool SupportsMerge => IsInitialized && ProductInfo.ParsedVersion?.Major >= 2;
    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 3;
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion?.Major >= 2;
    public override bool SupportsJsonTypes => false;
    public override bool SupportsArrayTypes => true;
    public override bool SupportsInsertReturning => true;

    public override string? UpsertIncomingAlias => "src";

    public override string UpsertIncomingColumn(string columnName)
    {
        var alias = UpsertIncomingAlias ?? "src";
        var wrappedAlias = WrapObjectName(alias);
        var prefix = string.IsNullOrWhiteSpace(wrappedAlias) ? string.Empty : $"{wrappedAlias}.";
        return $"{prefix}{WrapObjectName(columnName)}";
    }

    public override string GetLastInsertedIdQuery()
    {
        throw new NotSupportedException(
            "Firebird requires generator-specific syntax. Use RETURNING clause or GEN_ID(generator_name, 0) instead.");
    }

    private const string EngineVersionQuery =
        "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database";
    private const string DatabaseInfoQuery = "SELECT * FROM rdb$database";
    private const string MonitorVersionQuery = "SELECT mon$server_version FROM mon$database";
    private const string DefaultSessionSettings =
        "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;\nSET SQL DIALECT 3;";

    public override string GetVersionQuery()
    {
        return EngineVersionQuery;
    }

    public override string GetNaturalKeyLookupQuery(string tableName, string idColumnName,
        IReadOnlyList<string> columnNames, IReadOnlyList<string> parameterNames)
    {
        var query = base.GetNaturalKeyLookupQuery(tableName, idColumnName, columnNames, parameterNames);
        return query.Replace(" LIMIT 1", " ROWS 1", StringComparison.Ordinal);
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        // Firebird has no session-level read-only enforcement mechanism.
        // SET TRANSACTION READ ONLY starts a NEW transaction rather than modifying
        // the current one, so it cannot be used after IDbConnection.BeginTransaction().
        // Read-only enforcement would require provider-specific FbTransactionOptions,
        // which is outside the generic ADO.NET abstraction.
        return DefaultSessionSettings;
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return DefaultSessionSettings;
    }


    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        // Prefer scalar results to match fakeDb test helpers
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = EngineVersionQuery;
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
                cmd.CommandText = DatabaseInfoQuery;
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

        var legacyMatch = Regex.Match(versionString, @"LI-V(\d+)\.(\d+)\.(\d+)");
        if (legacyMatch.Success)
        {
            if (int.TryParse(legacyMatch.Groups[1].Value, out var major) &&
                int.TryParse(legacyMatch.Groups[2].Value, out var minor) &&
                int.TryParse(legacyMatch.Groups[3].Value, out var build))
            {
                return new Version(major, minor, build);
            }
        }

        var firebirdMatch = Regex.Match(versionString, @"Firebird\s+(\d+)\.(\d+)");
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
            cmd.CommandText = EngineVersionQuery;
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
            cmd.CommandText = MonitorVersionQuery;
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

    // Connection pooling properties for Firebird
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "MinPoolSize";
    public override string? MaxPoolSizeSettingName => "MaxPoolSize";
}
