// =============================================================================
// FILE: OracleDialect.cs
// PURPOSE: Oracle Database specific dialect implementation.
//
// AI SUMMARY:
// - Supports Oracle Database 12c+ with enterprise feature support.
// - Key features:
//   * MERGE statement for upserts (with RETURNING via dual table)
//   * Parameter marker: : (colon prefix, ODP.NET standard)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 64000 (practical limit)
//   * Sequence-based ID generation
// - Uses Oracle-specific RETURNING INTO clause via PL/SQL block.
// - Statement cache preferred over manual prepare.
// - Stored procedure support via Oracle anonymous blocks.
// - Parameter name limit: 30 chars (pre-12.2), 128 chars (12.2+).
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// Oracle Database dialect with comprehensive enterprise features.
/// </summary>
/// <remarks>
/// <para>
/// Supports Oracle Database 12c and later with automatic version detection.
/// Uses Oracle-specific syntax for sequences, upserts, and returning values.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement with optional RETURNING via PL/SQL.
/// </para>
/// <para>
/// <strong>Parameters:</strong> Uses colon prefix (:param) with ODP.NET naming.
/// </para>
/// </remarks>
internal class OracleDialect : SqlDialect
{
    internal OracleDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Oracle;
    public override string ParameterMarker => ":";

    public override bool SupportsNamedParameters => true;

    // IMMUTABLE: Oracle bind variable limit: we follow 64,000 as a practical upper bound
    // for modern Oracle (12c+) engines and ODP.NET providers. This aligns with
    // widely observed limits in production and avoids overly conservative caps.
    // Do not change without verifying against official Oracle docs/provider behavior.
    public override int MaxParameterLimit => 64000;

    // IMMUTABLE: Oracle output parameter limit - do not change without extensive testing
    public override int MaxOutputParameters => 1024;

    // IMMUTABLE: Oracle pre-12.2 identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 30;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Oracle;
    public override bool RequiresStoredProcParameterNameMatch => true;

    // Oracle prefers statement cache and array binding over manual prepare
    public override bool PrepareStatements => false;

    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    public override bool SupportsMerge => true;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 12;
    public override bool SupportsIdentityColumns => true;
    public override bool SupportsSavepoints => true;
    public override bool SupportsInsertReturning => true;

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"RETURNING {WrapObjectName(idColumnName)} INTO :1";
    }

    public override string GetLastInsertedIdQuery()
    {
        // Oracle typically uses sequences; this is a placeholder that would need sequence name
        throw new NotSupportedException(
            "Oracle requires sequence-specific syntax. Use RETURNING clause or sequence.CURRVAL instead.");
    }

    public override string GetVersionQuery()
    {
        return "SELECT * FROM v$version WHERE banner LIKE 'Oracle%'";
    }

    public override string GetNaturalKeyLookupQuery(string tableName, string idColumnName,
        IReadOnlyList<string> columnNames, IReadOnlyList<string> parameterNames)
    {
        const string rownumClause = " AND ROWNUM = 1";
        var query = base.GetNaturalKeyLookupQuery(tableName, idColumnName, columnNames, parameterNames);

        if (query.EndsWith(rownumClause, StringComparison.Ordinal))
        {
            query = query[..^rownumClause.Length];
        }

        return $"{query.TrimEnd()} FETCH FIRST 1 ROWS ONLY";
    }

    public override string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        const string baseSettings = "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';";

        if (readOnly)
        {
            return $"{baseSettings}\nALTER SESSION SET READ ONLY;";
        }

        return baseSettings;
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';";
    }

    public override void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        // Configure Oracle-specific connection settings for optimal performance
        if (connection.GetType().FullName?.Contains("Oracle") == true)
        {
            try
            {
                // Set StatementCacheSize for better performance with repeated queries
                var connectionType = connection.GetType();
                var statementCacheSizeProperty = connectionType.GetProperty("StatementCacheSize");
                if (statementCacheSizeProperty != null)
                {
                    var currentCacheSize = statementCacheSizeProperty.GetValue(connection);
                    if (currentCacheSize is int size && size < 64)
                    {
                        statementCacheSizeProperty.SetValue(connection, 64);
                    }
                }

                Logger.LogDebug("Applied Oracle connection settings: StatementCacheSize configured");
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to configure Oracle connection settings, using defaults");
            }
        }
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql2003;
        }

        return version.Major switch
        {
            >= 21 => SqlStandardLevel.Sql2016,
            >= 19 => SqlStandardLevel.Sql2016,
            >= 18 => SqlStandardLevel.Sql2011,
            >= 12 => SqlStandardLevel.Sql2008,
            >= 11 => SqlStandardLevel.Sql2003,
            _ => SqlStandardLevel.Sql99
        };
    }

    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        try
        {
            using var sc = transaction.CreateSqlContainer("ALTER SESSION SET READ ONLY;");
            sc.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to apply Oracle read-only session settings");
        }
    }

    // Connection pooling properties for Oracle
    public override bool SupportsExternalPooling => true;
    public override string? PoolingSettingName => "Pooling";
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";
    internal override int DefaultMaxPoolSize => 100;
}