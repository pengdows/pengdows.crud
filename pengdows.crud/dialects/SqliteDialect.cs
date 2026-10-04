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
//   * Max parameters: 999, or 32766 on 3.32+ (SQLITE_MAX_VARIABLE_NUMBER defaults)
//   * Prepared statements enabled
// - Connection mode detection:
//   * Isolated :memory: -> SingleConnection mode
//   * File mode -> SingleWriter mode
//   * Shared-cache in-memory -> SingleWriter mode
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
using pengdows.crud.@internal;
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
    internal override bool EnforcesReadOnlyTransactions => true;

    private readonly bool _systemDataSqlite;

    internal SqliteDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
        var ns = factory.GetType().Namespace ?? string.Empty;
        _systemDataSqlite = ns.Contains("System.Data.SQLite", StringComparison.OrdinalIgnoreCase);
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Sqlite;

    /// <inheritdoc />
    /// <remarks>Embedded, single-writer engine — not client-server.</remarks>
    public override bool IsClientServerDatabase => false;

    /// <inheritdoc />
    public override bool IsEmbeddedSingleWriterEngine => true;

    /// <inheritdoc />
    /// <remarks>
    /// <c>:memory:</c> (or a bare <c>mode=memory</c>/<c>filename=:memory:</c>/<c>datasource=:memory:</c>
    /// data source) is Isolated unless paired with <c>cache=shared</c>, in which case it's Shared.
    /// </remarks>
    public override InMemoryKind DetectInMemoryKind(string? connectionString)
    {
        var cs = (connectionString ?? string.Empty).Trim();
        var normalized = cs.ToLowerInvariant().Replace(" ", string.Empty);

        var dataSource = ExtractDataSourcePath(connectionString ?? string.Empty) ?? string.Empty;
        var dataSourceLower = dataSource.ToLowerInvariant();
        var dataSourceIsMemory = dataSourceLower.Contains(":memory:");
        var modeMem = normalized.Contains("mode=memory") ||
                      normalized.Contains("filename=:memory:") ||
                      normalized.Contains("datasource=:memory:") ||
                      dataSourceIsMemory;
        if (!modeMem)
        {
            return InMemoryKind.None;
        }

        var cacheShared = normalized.Contains("cache=shared");
        var dsIsLiteralMem = dataSourceIsMemory ||
                              normalized.Contains("datasource=:memory:") ||
                              normalized.Contains("filename=:memory:");
        if (cacheShared && !dsIsLiteralMem)
        {
            return InMemoryKind.Shared; // e.g., file:name?mode=memory&cache=shared
        }

        return InMemoryKind.Isolated;
    }

    /// <inheritdoc />
    public override (DbMode Mode, string Reason) CoerceConnectionMode(DbMode requested, string? connectionString,
        bool isLocalDb) =>
        CoerceEmbeddedSingleWriterMode(requested, DetectInMemoryKind(connectionString));

    private string? ExtractDataSourcePath(string connectionString)
    {
        try
        {
            var csb = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return csb.ContainsKey(ConnectionStringHelper.DataSourceKey)
                ? csb[ConnectionStringHelper.DataSourceKey]?.ToString()
                : connectionString;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not parse the SQLite connection string; treating it as the data source.");
            return connectionString;
        }
    }

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

    // SQLite supports LIMIT/OFFSET only — no OFFSET/FETCH NEXT syntax.
    public override bool SupportsOffsetFetch => false;

    // SQLite has no native UUID type — store GUIDs as 36-char hyphenated strings.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsOnConflictWhere => true; // DO UPDATE ... WHERE (SQLite 3.24+)
    public override bool SupportsMerge => false;
    public override bool SupportsSavepoints => true;

    // SQLite does not enforce FK constraints by default (PRAGMA foreign_keys = ON is required).
    // Unique and check constraint support is limited in older versions and not tested here.
    public override bool EnforcesForeignKeyConstraints => false;
    public override bool SupportsUniqueConstraints => false;
    public override bool SupportsCheckConstraints => false;

    // SQLite has no native BOOLEAN type; store as INTEGER.
    public override DbType BooleanDbType => DbType.Int32;
    public override bool SupportsJsonTypes => IsVersionAtLeast(3, 45);
    public override bool SupportsWindowFunctions => IsVersionAtLeast(3, 25);
    public override bool SupportsCommonTableExpressions => IsVersionAtLeast(3, 8, 3);

    public override bool SupportsInsertReturning => IsVersionAtLeast(3, 35);

    // TYPE-003, confirmed live 2026-09-30: SByte/UInt16/UInt32 widen to signed integers, which
    // SQLite stores as-is; UInt64 is bound as exact text in CreateDbParameter (the provider casts it
    // to Int64 unchecked: ulong.MaxValue was stored as -1).
    internal override bool BindsSByteAndUnsignedNatively => false;

    /// <summary>
    /// SQLite 3.35+ uses the inline RETURNING plan (best option, atomic).
    /// Older SQLite falls to CompoundStatement: INSERT ...; SELECT last_insert_rowid()
    /// to ensure both statements run on the same connection, preventing the two-lease
    /// correctness hazard of the SessionScopedFunction plan.
    /// Microsoft.Data.Sqlite supports multiple statements in a single ExecuteReader call
    /// without any connection string changes.
    /// </summary>
    public override GeneratedKeyPlan GetGeneratedKeyPlan()
        => SupportsInsertReturning ? GeneratedKeyPlan.Returning : GeneratedKeyPlan.CompoundStatement;

    /// <inheritdoc/>
    public override string GetCompoundInsertIdSuffix() => "; SELECT last_insert_rowid()";

    public override string GetLastInsertedIdQuery()
    {
        return "SELECT last_insert_rowid()";
    }

    public override string GetVersionQuery()
    {
        return "SELECT sqlite_version()";
    }

    // foreign_keys is per-connection state, so it is set on every checkout. journal_mode = WAL is
    // stored in the database file: it runs once per context (GetDatabaseInitializationSql), on a
    // write-capable connection only (REL-005; it ran on every checkout, read-only ones included).
    public override string GetBaseSessionSettings()
    {
        return "PRAGMA foreign_keys = ON;";
    }

    internal override string? GetDatabaseInitializationSql() => "PRAGMA journal_mode = WAL;";

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
            ? InternalConnectionStringAccess.GetRawConnectionString(context)
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

    internal override async Task<string?> GetProductNameCoreAsync(ITrackedConnection connection, bool useAsync)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT sqlite_version()";
            await using var reader = useAsync
                ? await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false)
                : cmd.ExecuteReader(CommandBehavior.SingleRow);

            if (useAsync ? await reader.ReadAsync().ConfigureAwait(false) : reader.Read())
            {
                return "SQLite";
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "SQLite product-name probe failed.");
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
        if (ex is not DbException dbEx)
        {
            return false;
        }

        return dbEx.ErrorCode == 1555 ||
               dbEx.ErrorCode == 2067 ||
               (dbEx.ErrorCode == 19 &&
                (dbEx.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                 dbEx.Message.Contains("PRIMARY KEY constraint failed", StringComparison.OrdinalIgnoreCase))) ||
               dbEx.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
               dbEx.Message.Contains("PRIMARY KEY constraint failed", StringComparison.OrdinalIgnoreCase);
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

        // Microsoft.Data.Sqlite stores a TimeSpan bound as DbType.Time as '' (confirmed with a real
        // in-memory database, TYPE-001), so bind the canonical "c" text instead.
        if (value is TimeSpan span)
        {
            return base.CreateDbParameter(name, DbType.String, span.ToString("c", CultureInfo.InvariantCulture));
        }

        // A Guid declared Binary is stored as its 16 bytes (base CreateDbParameter, TYPE-002).
        if (value is Guid guid && type != DbType.Binary)
        {
            return base.CreateDbParameter(name, DbType.String, guid.ToString("D"));
        }

        // TYPE-003: SQLite's integer storage stops at long.MaxValue, the provider casts a UInt64 to
        // Int64 unchecked, and decimal binding goes through double, so a UInt64 binds as exact text
        // (declare the column TEXT); reads parse it back exactly.
        if (type == DbType.UInt64)
        {
            return base.CreateDbParameter<object?>(name, DbType.String,
                value is null || value is DBNull
                    ? null
                    : Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
        }

        // Only when the caller declared DbType.Decimal; other mismatches (e.g. DbType.String +
        // decimal) fall through to the base validator and throw.
        if (value is decimal decValue && type == DbType.Decimal)
        {
            return BindDecimal(name, decValue);
        }

        var p = base.CreateDbParameter(name, type, value);

        if (value is byte[] bytes && (type == DbType.Binary || type == DbType.Object))
        {
            p.Size = bytes.Length;
        }

        return p;
    }

    // A decimal reassigned through SetParameterValue gets the value CreateDbParameter binds, whichever
    // of the two representations the parameter was created with (DbTypeForReassignedValue re-types it).
    public override object? PrepareParameterValue(object? value, DbType dbType) =>
        value is decimal d && dbType is DbType.Decimal or DbType.Double or DbType.String
            ? AsExactDouble(d) is { } dbl ? dbl : d.ToString(CultureInfo.InvariantCulture)
            : base.PrepareParameterValue(value, dbType);

    // System.Data.SQLite binds by DbType, not by the value's type (confirmed live), so the DbType
    // must follow the representation PrepareParameterValue chose.
    internal override DbType? DbTypeForReassignedValue(object? newValue, object? preparedValue) =>
        newValue is decimal
            ? preparedValue is double ? DbType.Double : DbType.String
            : null;

    // A decimal binds as a REAL when a double holds it exactly, else as exact invariant text.
    // TYPE-002, confirmed live (Microsoft.Data.Sqlite 9): a decimal bound as a double lost its digits
    // (-1234567890123456.0123456789 was stored as "-1.23456789012346e+15"); text keeps every digit
    // in a TEXT column. But text has no affinity, and SQLite converts neither side of a comparison
    // when neither has one, so text compared with an arithmetic or aggregate result
    // (`price * qty > @p`, `SUM(x) > @p`) never matched (REV-058, confirmed live).
    private DbParameter BindDecimal(string? name, decimal value) =>
        AsExactDouble(value) is { } dbl
            ? base.CreateDbParameter(name, DbType.Double, dbl)
            : base.CreateDbParameter(name, DbType.String, value.ToString(CultureInfo.InvariantCulture));

    private static double? AsExactDouble(decimal value)
    {
        var dbl = (double)value;
        return (decimal)dbl == value ? dbl : null;
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

    public override bool IsForeignKeyViolation(DbException ex) =>
        ex.ErrorCode == 787 ||
        ex.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);

    public override bool IsNotNullViolation(DbException ex) =>
        ex.ErrorCode == 1299 ||
        ex.Message.Contains("NOT NULL constraint failed", StringComparison.OrdinalIgnoreCase);

    public override bool IsCheckConstraintViolation(DbException ex) =>
        ex.ErrorCode == 275 ||
        ex.Message.Contains("CHECK constraint failed", StringComparison.OrdinalIgnoreCase);

    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        var errorCode = TryGetProviderErrorCode(ex);

        // SQLITE_READONLY = 8: write attempted on a read-only connection.
        // SqliteExceptionTranslator already classifies this as ReadOnlyViolation.
        if (errorCode == 8)
        {
            category = DbErrorCategory.ReadOnlyViolation;
            return true;
        }

        // Checked as a generic category-level fallback, distinct from IsUniqueViolation/
        // IsForeignKeyViolation/IsNotNullViolation/IsCheckConstraintViolation (already checked
        // earlier in SqlDialect.ClassifyException, before this method is ever called) — those four
        // each require a specific extended result code (1555/2067/787/1299/275) or specific message
        // wording to identify ONE kind, so a bare SQLITE_CONSTRAINT (19, with no more specific
        // extended code and no matching message text) matches none of them individually but is
        // still, generically, a constraint violation.
        if (errorCode == 19 ||
            errorCode == 1555 ||
            errorCode == 2067 ||
            (errorCode is not null && (errorCode.Value & 0xFF) == 19))
        {
            category = DbErrorCategory.ConstraintViolation;
            return true;
        }

        category = DbErrorCategory.Unknown;
        return false;
    }
}
