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
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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
    public override bool SupportsRepeatedNamedParameters => false;

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

    /// <inheritdoc />
    public override void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
        ISqlQueryBuilder query)
    {
        BuildBatchInsertSql(tableName, columnNames, rowCount, query, null);
    }

    /// <inheritdoc />
    public override void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
        ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name cannot be null or empty.", nameof(tableName));
        }

        if (columnNames == null || columnNames.Count == 0)
        {
            throw new ArgumentException("Column names cannot be null or empty.", nameof(columnNames));
        }

        if (rowCount <= 0)
        {
            throw new ArgumentException("Row count must be greater than zero.", nameof(rowCount));
        }

        // Oracle uses INSERT ALL INTO table (cols) VALUES (...) INTO table (cols) VALUES (...) SELECT 1 FROM DUAL
        query.Append("INSERT ALL ");

        var colList = string.Join(", ", columnNames);

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            query.Append("INTO ");
            query.Append(tableName);
            query.Append(" (");
            query.Append(colList);
            query.Append(") VALUES (");

            for (var col = 0; col < columnNames.Count; col++)
            {
                if (col > 0)
                {
                    query.Append(", ");
                }

                var val = getValue?.Invoke(row, col);
                if (val == null || val == DBNull.Value)
                {
                    query.Append("NULL");
                }
                else
                {
                    query.Append(ParameterMarker);
                    query.Append('b');
                    query.Append(paramIdx++.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            query.Append(") ");
        }

        query.Append("SELECT 1 FROM DUAL");
    }

    public override bool SupportsMerge => true;

    // Oracle does not support DROP TABLE IF EXISTS — requires PL/SQL exception handling.
    public override bool SupportsDropTableIfExists => false;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 12;
    public override bool SupportsIdentityColumns => true;
    public override bool SupportsSavepoints => true;
    public override bool SupportsInsertReturning => true;

    public override string RenderMergeSource(IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> parameterNames)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (parameterNames == null)
        {
            throw new ArgumentNullException(nameof(parameterNames));
        }

        if (columns.Count != parameterNames.Count)
        {
            throw new ArgumentException("Column and parameter counts must match.");
        }

        var select = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                select.Append(", ");
            }

            var placeholder = MakeParameterName(parameterNames[i]);
            if (columns[i].IsJsonType)
            {
                placeholder = RenderJsonArgument(placeholder, columns[i]);
            }

            select.Append(placeholder);
            select.Append(" AS ");
            select.Append(WrapObjectName(columns[i].Name));
        }

        return string.Concat("USING (SELECT ", select.ToString(), " FROM DUAL) s");
    }

    public override string RenderMergeOnClause(string predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return string.Concat("(", predicate, ")");
    }

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"RETURNING {WrapObjectName(idColumnName)} INTO :1";
    }

    /// <summary>
    /// Oracle RETURNING INTO requires a named output parameter bound by the driver,
    /// not a generic positional placeholder. Override base to provide the correct syntax.
    /// Note: In normal operation Oracle uses PrefetchSequence (GetGeneratedKeyPlan returns
    /// PrefetchSequence), so this path is only reached when the caller explicitly renders
    /// the clause.
    /// </summary>
    public override string RenderInsertReturningClause(string idColumnWrapped)
    {
        return $" RETURNING {idColumnWrapped} INTO :1";
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

    private const string NlsDateFormatSetting = "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';";
    private const string ReadOnlySessionSetting = "ALTER SESSION SET READ ONLY;";
    private const string ReadWriteSessionSetting = "ALTER SESSION SET READ WRITE;";

    public override string GetBaseSessionSettings()
    {
        return NlsDateFormatSetting;
    }

    public override string GetReadOnlySessionSettings()
    {
        return ReadOnlySessionSetting;
    }

    internal override string? GetReadOnlyTransactionResetSql()
    {
        return ReadWriteSessionSetting;
    }

    internal override void ApplyConnectionSettingsCore(
        IDbConnection connection,
        IDatabaseContext context,
        bool readOnly,
        string? connectionStringOverride)
    {
        base.ApplyConnectionSettingsCore(connection, context, readOnly, connectionStringOverride);

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
        TryExecuteReadOnlySql(transaction, ReadOnlySessionSetting, "Oracle");
    }

    public override ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        return TryExecuteReadOnlySqlAsync(transaction, ReadOnlySessionSetting, "Oracle", cancellationToken);
    }

    // Connection pooling properties for Oracle
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";
}
