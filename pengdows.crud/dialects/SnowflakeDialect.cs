// =============================================================================
// FILE: SnowflakeDialect.cs
// PURPOSE: Snowflake-specific SQL dialect implementation.
//
// AI SUMMARY:
// - Supports Snowflake cloud data warehouse with SQL:2016 compliance.
// - Key features:
//   * Parameter marker: : (colon prefix, Snowflake.Data standard)
//   * Identifier quoting: "name" (double-quotes; unquoted identifiers fold to UPPERCASE)
//   * MERGE statement for upserts (uses src.{col} alias pattern)
//   * INSERT ... RETURNING for generated key retrieval
//   * Savepoints supported
//   * No Docker image available; uses credential-based external connection
// - Uses plain DbType mappings (Snowflake.Data driver, not Npgsql)
// - PrepareStatements enabled for performance
// - SupportsNamespaces: true (db.schema.table fully qualified)
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// Snowflake dialect with SQL:2016 compliance and MERGE-based upsert.
/// </summary>
/// <remarks>
/// <para>
/// Supports the Snowflake cloud data warehouse via the official Snowflake.Data .NET connector.
/// Uses colon-prefixed named parameters and double-quote identifier quoting.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement with <c>src.{col}</c> alias for incoming values.
/// </para>
/// <para>
/// <strong>Prepared Statements:</strong> Enabled by default for performance.
/// </para>
/// </remarks>
internal class SnowflakeDialect : SqlDialect
{
    internal SnowflakeDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Snowflake;

    // Snowflake.Data uses colon-prefixed named parameters
    public override string ParameterMarker => ":";
    public override bool SupportsNamedParameters => true;

    // Double-quote identifiers; unquoted Snowflake identifiers fold to UPPERCASE
    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";

    // Snowflake has strong SQL:2016 compliance
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : SqlStandardLevel.Sql2016;

    public override bool PrepareStatements => true;

    public override bool SupportsNamespaces => true;

    // MERGE-based upsert (no ON CONFLICT)
    public override bool SupportsMerge => true;
    public override bool SupportsMergeReturning => false;
    public override bool SupportsInsertOnConflict => false;

    // INSERT ... RETURNING is supported
    public override bool SupportsInsertReturning => true;

    public override bool SupportsSavepoints => false;

    public override bool SupportsDropTableIfExists => true;

    // Snowflake MERGE: target alias is allowed on UPDATE SET columns
    public override bool MergeUpdateRequiresTargetAlias => true;

    public override string? UpsertIncomingAlias => "src";

    public override string UpsertIncomingColumn(string columnName)
    {
        return $"src.{WrapObjectName(columnName)}";
    }

    public override string GetVersionQuery()
    {
        return "SELECT CURRENT_VERSION()";
    }

    public override System.Version? ParseVersion(string versionString)
    {
        // Snowflake reports versions like "7.43.0" or "8.12.1"
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        if (System.Version.TryParse(versionString.Trim(), out var v))
        {
            return v;
        }

        return base.ParseVersion(versionString);
    }

    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>
        {
            { 8, SqlStandardLevel.Sql2019 },
            { 7, SqlStandardLevel.Sql2016 }
        };
    }

    public override SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql2016;
    }

    public override object? PrepareParameterValue(object? value, DbType dbType)
    {
        if (value is DateTimeOffset dto)
        {
            // Snowflake TIMESTAMP_NTZ does not store offsets; normalize to UTC instant.
            return dto.UtcDateTime;
        }

        return base.PrepareParameterValue(value, dbType);
    }

    /// <summary>
    /// Pass through — Snowflake.Data handles warehouse/role/schema in the connection string.
    /// </summary>
    internal override string PrepareConnectionStringForDataSource(string connectionString)
    {
        return connectionString;
    }

    // Connection pooling properties for Snowflake
    public override string? MinPoolSizeSettingName => "minPoolSize";
    public override string? MaxPoolSizeSettingName => "maxPoolSize";
    public override string? ApplicationNameSettingName => "application";
}