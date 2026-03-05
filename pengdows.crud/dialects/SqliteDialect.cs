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
using pengdows.crud.infrastructure;
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
    public override int MaxParameterLimit => IsVersionAtLeast(3, 32) ? 32766 : 999;

    // SQLite has no server-side row-per-batch limit; only the parameter limit constrains chunk size
    public override int MaxRowsPerBatch => int.MaxValue;

    // IMMUTABLE: SQLite identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 255;

    // SQLite benefits from prepared statements with inherent prepare support
    public override bool PrepareStatements => true;

    // SQLite has no native UUID type — store GUIDs as 36-char hyphenated strings.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => false;
    public override bool SupportsSavepoints => true;
    public override bool SupportsJsonTypes => IsVersionAtLeast(3, 45);
    public override bool SupportsWindowFunctions => IsVersionAtLeast(3, 25);
    public override bool SupportsCommonTableExpressions => IsVersionAtLeast(3, 8, 3);

    public override bool SupportsInsertReturning => IsVersionAtLeast(3, 35);

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
        return "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
    }

    // Read-only enforcement for SQLite uses Mode=ReadOnly in the connection string (see
    // GetReadOnlyConnectionString and ApplyConnectionSettingsCore), which opens the database
    // file read-only at the OS level. This is stronger and more reliable than PRAGMA query_only,
    // which is session-scoped and can be reset by any caller on the same connection.
    // No session SQL or transaction SQL is used for read-only enforcement.
    public override string? GetReadOnlyConnectionParameter()
    {
        return "Mode=ReadOnly";
    }

    internal override void ApplyConnectionSettingsCore(
        IDbConnection connection,
        IDatabaseContext context,
        bool readOnly,
        string? connectionStringOverride)
    {
        var baseConnectionString = string.IsNullOrWhiteSpace(connectionStringOverride)
            ? context.ConnectionString
            : connectionStringOverride;

        // SQLite: Only apply read-only connection parameter if not a memory database
        if (readOnly && IsMemoryDatabase(baseConnectionString))
        {
            // For memory databases, just set the connection string without read-only parameter
            connection.ConnectionString = baseConnectionString;
        }
        else
        {
            // Use base class implementation for non-memory databases
            base.ApplyConnectionSettingsCore(connection, context, readOnly, baseConnectionString);
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
        if (value is bool b && IsNumericDbType(type))
        {
            return base.CreateDbParameter(name, type, b ? 1 : 0);
        }

        if (value is DateTime dt)
        {
            var utc = NormalizeUtc(dt);
            return base.CreateDbParameter(name, DbType.String, utc.ToString("o", CultureInfo.InvariantCulture));
        }

        if (value is DateTimeOffset dto)
        {
            return base.CreateDbParameter(name, DbType.String, dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        }

        // SQLite stores DECIMAL as REAL (64-bit double). Microsoft.Data.Sqlite cannot bind
        // DbType.Decimal correctly — the driver stores 0 instead of the actual value.
        // Only convert when the caller declared DbType.Decimal; other mismatches (e.g.
        // DbType.String + decimal) fall through to the base validator so they still throw.
        if (value is decimal decValue && type == DbType.Decimal)
        {
            return base.CreateDbParameter(name, DbType.Double, (double)decValue);
        }

        var p = base.CreateDbParameter(name, type, value);

        if (value is byte[] bytes && (type == DbType.Binary || type == DbType.Object))
        {
            p.Size = bytes.Length;
        }

        return p;
    }

    public override object? PrepareParameterValue(object? value, DbType dbType)
    {
        return base.PrepareParameterValue(value, dbType);
    }

    private static bool IsNumericDbType(DbType type)
    {
        return type is DbType.Byte or DbType.SByte
            or DbType.Int16 or DbType.UInt16
            or DbType.Int32 or DbType.UInt32
            or DbType.Int64 or DbType.UInt64
            or DbType.Single or DbType.Double
            or DbType.Decimal or DbType.Currency
            or DbType.VarNumeric;
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
