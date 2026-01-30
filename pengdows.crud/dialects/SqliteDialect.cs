// =============================================================================
// FILE: SqliteDialect.cs
// PURPOSE: SQLite specific dialect implementation.
//
// AI SUMMARY:
// - Supports SQLite 3.24+ with good SQL standard compliance.
// - Key features:
//   * INSERT ... ON CONFLICT for upserts (no MERGE support)
//   * Parameter marker: @ (at sign)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 999 (SQLITE_MAX_VARIABLE_NUMBER default)
//   * Prepared statements enabled
// - Connection mode detection:
//   * :memory: -> SingleConnection mode
//   * File mode -> SingleWriter mode
//   * Shared cache -> appropriate mode
// - Detects System.Data.SQLite vs Microsoft.Data.Sqlite provider.
// - RETURNING clause for getting generated IDs (SQLite 3.35+).
// - Savepoint support for nested transaction semantics.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// SQLite dialect with good SQL standard compliance.
/// </summary>
/// <remarks>
/// <para>
/// Supports SQLite 3.24+ for UPSERT and 3.35+ for RETURNING clause.
/// </para>
/// <para>
/// <strong>Connection Modes:</strong> DatabaseContext automatically selects
/// appropriate DbMode based on connection string:
/// </para>
/// <list type="bullet">
/// <item><description><c>:memory:</c> - Uses SingleConnection mode</description></item>
/// <item><description>File path - Uses SingleWriter mode</description></item>
/// </list>
/// <para>
/// <strong>UPSERT:</strong> Uses INSERT ... ON CONFLICT DO UPDATE.
/// </para>
/// </remarks>
internal class SqliteDialect : SqlDialect
{
    private readonly bool _systemDataSqlite;

    internal SqliteDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
        var ns = factory.GetType().Namespace ?? string.Empty;
        _systemDataSqlite = ns.Contains("System.Data.SQLite", StringComparison.OrdinalIgnoreCase);
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Sqlite;
    public override string ParameterMarker => "@";

    public override bool SupportsNamedParameters => true;

    // IMMUTABLE: SQLite SQLITE_MAX_VARIABLE_NUMBER default - do not change without extensive testing
    public override int MaxParameterLimit => 999;

    // IMMUTABLE: SQLite identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 255;

    // SQLite benefits from prepared statements with inherent prepare support
    public override bool PrepareStatements => true;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => false;
    public override bool SupportsSavepoints => true;
    public override bool SupportsJsonTypes => IsVersionAtLeast(3, 45);
    public override bool SupportsWindowFunctions => IsVersionAtLeast(3, 25);
    public override bool SupportsCommonTableExpressions => IsVersionAtLeast(3, 8, 3);

    public override bool SupportsInsertReturning => IsVersionAtLeast(3, 35);

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"RETURNING {WrapObjectName(idColumnName)}";
    }

    public override string GetLastInsertedIdQuery()
    {
        return "SELECT last_insert_rowid()";
    }

    public override string GetVersionQuery()
    {
        return "SELECT sqlite_version()";
    }

    public override string GetBaseSessionSettings()
    {
        return "PRAGMA foreign_keys = ON;";
    }

    public override string GetReadOnlySessionSettings()
    {
        return "PRAGMA query_only = ON;";
    }

    public override string? GetReadOnlyConnectionParameter()
    {
        return "Mode=ReadOnly";
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return "PRAGMA foreign_keys = ON;";
    }

    public override void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        // SQLite: Only apply read-only connection parameter if not a memory database
        if (readOnly && IsMemoryDatabase(context.ConnectionString))
        {
            // For memory databases, just set the connection string without read-only parameter
            connection.ConnectionString = context.ConnectionString;
        }
        else
        {
            // Use base class implementation for non-memory databases
            base.ApplyConnectionSettings(connection, context, readOnly);
        }
    }

    internal override string GetReadOnlyConnectionString(string connectionString)
    {
        return IsMemoryDatabase(connectionString)
            ? connectionString
            : base.GetReadOnlyConnectionString(connectionString);
    }

    protected override bool IsMemoryDatabase(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var lower = connectionString.ToLowerInvariant();
        return lower.Contains(":memory:") || lower.Contains("mode=memory");
    }

    public override DataTable GetDataSourceInformationSchema(ITrackedConnection connection)
    {
        var resourceName = $"pengdows.crud.xml.{SupportedDatabase.Sqlite}.schema.xml";
        using var stream = typeof(SqliteDialect).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");
        var table = new DataTable();
        table.ReadXml(stream);
        return table;
    }

    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT sqlite_version()";
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

            if (await reader.ReadAsync())
            {
                return "SQLite";
            }
        }
        catch
        {
        }

        return null;
    }

    public override string ExtractProductNameFromVersion(string versionString)
    {
        return "SQLite";
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql92;
        }

        if (version.Major == 3)
        {
            return version.Minor switch
            {
                >= 45 => SqlStandardLevel.Sql2016,
                >= 35 => SqlStandardLevel.Sql2011,
                >= 25 => SqlStandardLevel.Sql2008,
                >= 8 => SqlStandardLevel.Sql2003,
                _ => SqlStandardLevel.Sql92
            };
        }

        return SqlStandardLevel.Sql92;
    }

    public override bool IsUniqueViolation(DbException ex)
    {
        return ex is DbException dbEx && dbEx.ErrorCode == 19;
    }

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        if (value is DateTime dt)
        {
            var utc = NormalizeUtc(dt);
            return base.CreateDbParameter(name, DbType.String, utc.ToString("o", CultureInfo.InvariantCulture));
        }

        if (value is DateTimeOffset dto)
        {
            return base.CreateDbParameter(name, DbType.String,
                dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        }

        return base.CreateDbParameter(name, type, value);
    }

    // Connection pooling properties for SQLite (provider-aware)
    public override bool SupportsExternalPooling =>
        _systemDataSqlite; // Microsoft.Data.Sqlite: true pooling, but no min/max keywords

    public override string? PoolingSettingName => "Pooling"; // set only if absent; harmless for M.D.Sqlite
    public override string? MinPoolSizeSettingName => null; // no min keyword for either
    public override string? MaxPoolSizeSettingName => _systemDataSqlite ? "Max Pool Size" : null;
    internal override int DefaultMaxPoolSize => int.MaxValue;

    public override string UpsertIncomingColumn(string columnName)
    {
        return $"EXCLUDED.{WrapObjectName(columnName)}";
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}