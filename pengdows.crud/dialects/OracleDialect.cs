// =============================================================================
// FILE: OracleDialect.cs
// PURPOSE: Oracle Database specific dialect implementation.
//
// AI SUMMARY:
// - Supports Oracle Database 12c+ with enterprise feature support.
// - Key features:
//   * MERGE statement for upserts (source row via SELECT ... FROM DUAL)
//   * Parameter marker: : (colon prefix, ODP.NET standard)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 65535 (Oracle's internal 16-bit bind variable slot limit)
//   * Identity columns supported
// - Generated keys via RETURNING ... INTO an output parameter (GeneratedKeyPlan.Returning).
// - Statement cache preferred over manual prepare.
// - Stored procedure support via Oracle anonymous blocks.
// - Parameter name limit: 30 chars (pre-12.2), 128 chars (12.2+).
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.types.valueobjects;
using pengdows.crud.types;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// Oracle Database dialect with comprehensive enterprise features.
/// </summary>
/// <remarks>
/// <para>
/// Supports Oracle Database 12c and later with automatic version detection.
/// Uses Oracle-specific syntax for upserts and returning values.
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE statement with a <c>SELECT ... FROM DUAL</c> source.
/// </para>
/// <para>
/// <strong>Parameters:</strong> Uses colon prefix (:param) with ODP.NET naming.
/// </para>
/// </remarks>
internal class OracleDialect : SqlDialect
{
    internal override bool EnforcesReadOnlyTransactions => true;

    // TYPE-002: ODP.NET writes a Guid to RAW(16) in .NET's mixed-endian ToByteArray order.
    internal override bool StoresGuidBytesBigEndian => false;

    internal OracleDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Oracle;

    // Verified live: ODP.NET executes a single MERGE as one bare SQL statement (not inside a
    // PL/SQL block), and a trailing semicolon there is rejected as an invalid character —
    // "ORA-00933: SQL command not properly ended".
    public override bool RequiresMergeStatementTerminator => false;

    public override string ParameterMarker => ":";

    public override bool SupportsNamedParameters => true;
    public override bool SupportsRepeatedNamedParameters => false;

    // IMMUTABLE: Oracle bind variable limit is 65,535 — the hard ceiling imposed by Oracle's
    // internal 16-bit slot index for bind variables. Applies per SQL statement and per
    // PL/SQL procedure. In practice you will hit PGA memory or statement-length limits
    // well before this, but 65535 is the correct upper bound to enforce in chunking logic.
    public override int MaxParameterLimit => 65535;

    // IMMUTABLE: Oracle output parameter limit - do not change without extensive testing
    public override int MaxOutputParameters => 1024;

    // IMMUTABLE: Oracle pre-12.2 identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 30;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Oracle;
    public override bool RequiresStoredProcParameterNameMatch => true;

    // Oracle prefers statement cache and array binding over manual prepare
    public override bool PrepareStatements => false;

    // Oracle uses OFFSET/FETCH NEXT syntax (12c+) — no LIMIT keyword.
    public override bool SupportsLimitOffset => false;

    // Oracle stores GUIDs as VARCHAR2(36) — handled here via GuidFormat rather than
    // AdvancedTypeRegistry so the mapping is explicit, testable, and dialect-co-located.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    [Obsolete("MaxSupportedStandard is a coarse heuristic; query the specific Supports* capability instead.", false)]
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
        // INSERT ALL INTO t (cols) VALUES (...) INTO t (cols) VALUES (...) SELECT 1 FROM DUAL
        AppendStatementPerRowBatchInsert(tableName, columnNames, rowCount, query, getValue,
            "INSERT ALL ", "INTO ", ") ", "SELECT 1 FROM DUAL");
    }

    // FEAT-005: OracleCommand.ArrayBindCount lets one parameterized, single-row-shaped INSERT bind
    // each column to an array of per-row values — more efficient than the INSERT ALL multi-row
    // literal shape below for large row counts, and the only genuinely provider-native bulk-write
    // mechanism ODP.NET offers. See docs/planning/bulk-loading-design.md's Part 2.
    internal override bool SupportsArrayBinding => true;

    /// <inheritdoc/>
    internal override void ConfigureArrayBinding(DbCommand cmd, int rowCount)
    {
        // NOTE: "ArrayBindCount" is an intentional provider contract for Oracle.ManagedDataAccess —
        // not on the generic DbCommand surface, so reached via reflection, no hard package
        // reference. Same pattern as ApplyConnectionSettingsCore's StatementCacheSize hook.
        if (cmd.GetType().FullName?.Contains("Oracle") != true)
        {
            return;
        }

        try
        {
            var arrayBindCountProperty = cmd.GetType().GetProperty("ArrayBindCount");
            arrayBindCountProperty?.SetValue(cmd, rowCount);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to configure Oracle ArrayBindCount via reflection");
        }
    }

    public override bool SupportsBatchUpdate => true;

    // TYPE-003, confirmed live 2026-09-30: the provider rejects DbType.SByte and the unsigned DbTypes.
    internal override bool BindsSByteAndUnsignedNatively => false;

    // TYPE-015: VECTOR binds from "[...]" text; ODP.NET rejects a float[] unless the parameter is
    // OracleDbType.Vector (ORA-50028, confirmed live on Oracle 26ai with ODP.NET 23.8).
    internal override bool BindsVectorsAsText => true;

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        AppendBatchMerge(tableName, columnNames, keyColumns, rowCount, query, getValue, null);
    }

    internal override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo> columns)
    {
        AppendBatchMerge(tableName, columnNames, keyColumns, rowCount, query, getValue, columns);
    }

    // With columns, a value is written through RenderColumnArgument where the column asks for it,
    // a NULL too: a bare NULL in the UNION ALL source is typed as text, which an SDO_GEOMETRY target
    // refuses (ORA-00932, TYPE-021, confirmed live).
    private void AppendBatchMerge(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo>? columns)
    {
        if (rowCount <= 0)
        {
            return;
        }

        // Oracle has no VALUES(...) row-constructor table literal for the MERGE USING source
        // (unlike SQL Server/PostgreSQL), so the source is built the same way Oracle's own batch
        // INSERT already builds a multi-row shape: one "SELECT ... FROM DUAL" branch per row,
        // joined with UNION ALL. Column aliases are declared only on the first branch — standard
        // SQL (and Oracle) apply the first branch's aliases to the whole UNION ALL result.
        // Table aliases use no "AS" keyword (Oracle MERGE rejects it — see the shared single-row
        // upsert path in TableGateway.Upsert.cs), and no trailing ';' is appended: ODP.NET runs a
        // MERGE as one bare statement, and a trailing semicolon there is ORA-00911 (see
        // RequiresMergeStatementTerminator).
        query.Append("MERGE INTO ");
        query.Append(tableName);
        query.Append(" t USING (");

        var allCols = new List<string>(keyColumns);
        allCols.AddRange(columnNames);

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            if (row > 0)
            {
                query.Append(" UNION ALL ");
            }

            query.Append("SELECT ");
            for (var col = 0; col < allCols.Count; col++)
            {
                if (col > 0)
                {
                    query.Append(", ");
                }

                var val = getValue?.Invoke(row, col);
                var cell = val == null || val == DBNull.Value
                    ? "NULL"
                    : string.Concat(ParameterMarker, "b", paramIdx++.ToString(System.Globalization.CultureInfo.InvariantCulture));
                query.Append(columns != null && RendersColumnArgument(columns[col])
                    ? RenderColumnArgument(cell, columns[col])
                    : cell);

                if (row == 0)
                {
                    query.Append(" AS ");
                    query.Append(allCols[col]);
                }
            }

            query.Append(" FROM DUAL");
        }

        query.Append(") s ON (");
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (i > 0)
            {
                query.Append(" AND ");
            }

            query.Append("t.");
            query.Append(keyColumns[i]);
            query.Append(" = s.");
            query.Append(keyColumns[i]);
        }

        query.Append(") WHEN MATCHED THEN UPDATE SET ");
        for (var i = 0; i < columnNames.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(columnNames[i]);
            query.Append(" = s.");
            query.Append(columnNames[i]);
        }

        // [Version] entities never reach this path: the gateway updates them one row at a time
        // (the per-row UPDATE increments and checks the version).
    }

    public override bool SupportsMerge => true;

    // Oracle MERGE: "WHEN MATCHED THEN UPDATE SET ... WHERE cond"; there is no "WHEN MATCHED AND".
    public override bool MergeMatchedConditionAsUpdateWhere => true;

    // Oracle does not support DROP TABLE IF EXISTS — requires PL/SQL exception handling.
    public override bool SupportsDropTableIfExists => false;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion?.Major >= 12;
    public override bool SupportsIdentityColumns => true;
    public override bool SupportsSavepoints => true;

    // Oracle has no RELEASE SAVEPOINT statement at all — a savepoint is implicitly released on
    // commit/rollback or superseded by a later savepoint with the same name.
    public override SavepointCapabilities SavepointCapabilities =>
        SavepointCapabilities.Create | SavepointCapabilities.Rollback;

    public override bool SupportsInsertReturning => true;

    // RETURNING ... INTO binds the generated id through an ADO.NET output parameter, not a result set.
    public override bool RequiresOutputParameterForReturning => true;

    public override GeneratedKeyPlan GetGeneratedKeyPlan()
    {
        return GeneratedKeyPlan.Returning;
    }

    public override string RenderMergeSource(IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> parameterNames)
    {
        return RenderSelectMergeSource(columns, parameterNames, " FROM DUAL", " s");
    }

    public override string RenderMergeOnClause(string predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return string.Concat("(", predicate, ")");
    }

    /// <summary>
    /// Name of the OUT parameter that receives the generated key from RETURNING ... INTO. The
    /// placeholder is named, not positional (":1"), so it binds whether or not the command uses
    /// BindByName; the positional form only worked because the OUT parameter happened to be last.
    /// </summary>
    internal const string ReturningParameterName = "o0";

    public override string GetInsertReturningClause(string idColumnName)
    {
        return $"RETURNING {WrapObjectName(idColumnName)} INTO {MakeParameterName(ReturningParameterName)}";
    }

    /// <summary>
    /// Oracle RETURNING INTO requires a named output parameter bound by the driver,
    /// not a generic positional placeholder. Override base to provide the correct syntax.
    /// <see cref="GetGeneratedKeyPlan"/> returns Returning, so this is the normal generated-key path.
    /// </summary>
    public override string RenderInsertReturningClause(string idColumnWrapped)
    {
        return $" RETURNING {idColumnWrapped} INTO {MakeParameterName(ReturningParameterName)}";
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

    private const string SetTransactionReadOnlySql = "SET TRANSACTION READ ONLY;";

    // Oracle does not support multiple SQL statements in a single ADO.NET ExecuteNonQuery call.
    // Wrap all ALTER SESSION statements in a PL/SQL anonymous block so they execute atomically
    // in one round-trip without ORA-00911 (invalid character; from bare semicolons in SQL context).
    //
    // TIME_ZONE = 'UTC': forces session timezone to UTC so TIMESTAMP WITH TIME ZONE columns
    // store and return DateTime values correctly when the host machine uses a non-UTC timezone.
    // Without this, ODP.NET ignores DateTime.Kind=Utc and Oracle interprets the raw DateTime
    // bytes using the session's local offset, causing round-trip errors proportional to the offset.
    //
    // DBMS_APPLICATION_INFO.SET_MODULE: sets the MODULE column in V$SESSION so SQL Monitor and
    // AWR reports show the application name (including the -rw / -ro pool suffix).
    // Oracle does not support Application Name in the connection string, so this is the only way.
    // MODULE max length is 48 chars; names longer than that are truncated to avoid ORA-06502.

    public override string GetBaseSessionSettings(string? applicationName)
    {
        // Use SbLite (stack-allocated) for zero-heap string building.
        // Oracle MODULE limit is 48 chars of the actual value; truncate BEFORE escaping
        // so that single-quote escaping ('' pairs) doesn't push the value over the limit
        // and a truncation mid-pair doesn't produce broken PL/SQL string literals.
        using var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        sb.Append("BEGIN\n");
        sb.Append("  EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_DATE_FORMAT = ''YYYY-MM-DD''';\n");
        sb.Append("  EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_TIMESTAMP_FORMAT = ''YYYY-MM-DD HH24:MI:SS.FF''';\n");
        sb.Append("  EXECUTE IMMEDIATE 'ALTER SESSION SET TIME_ZONE = ''UTC''';\n");
        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            // Truncate to 48 chars FIRST (Oracle MODULE column limit), then in one
            // character-by-character pass: strip control characters (newlines, null
            // bytes, etc. that would break the PL/SQL string literal) and double
            // any single quotes. Single-pass avoids allocations and eliminates the
            // risk of a truncation splitting a doubled-quote pair.
            // Trust boundary: ApplicationName is developer configuration; sanitization
            // is defense-in-depth against accidental or injected control characters.
            var src = applicationName.Length > 48
                ? applicationName.AsSpan(0, 48)
                : applicationName.AsSpan();
            sb.Append("  DBMS_APPLICATION_INFO.SET_MODULE(module_name => '");
            foreach (var c in src)
            {
                if (char.IsControl(c))
                {
                    continue;
                }

                if (c == '\'')
                {
                    sb.Append('\'');
                    sb.Append('\'');
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append("', action_name => NULL);\n");
        }
        sb.Append("END;");
        return sb.ToString();
    }

    public override string GetReadOnlySessionSettings()
    {
        // Oracle has no true persistent session-level read-only mode.
        // Enforcement must happen at transaction start.
        return string.Empty;
    }

    internal override string? GetReadOnlyTransactionResetSql()
    {
        return string.Empty;
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
                // Set StatementCacheSize for better performance with repeated queries.
                // NOTE: "StatementCacheSize" is an intentional provider contract for Oracle.ManagedDataAccess.
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

    // Versions before 11 are SQL:1999; with no version, SQL:2003 (not the base default) (DRY-019).
    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping() => new()
    {
        [0] = SqlStandardLevel.Sql99,
        [11] = SqlStandardLevel.Sql2003,
        [12] = SqlStandardLevel.Sql2008,
        [18] = SqlStandardLevel.Sql2011,
        [19] = SqlStandardLevel.Sql2016
    };

    public override SqlStandardLevel DetermineStandardCompliance(Version? version) =>
        version == null ? SqlStandardLevel.Sql2003 : base.DetermineStandardCompliance(version);

    protected override string? ReadOnlyTransactionSql => SetTransactionReadOnlySql;

    // Connection pooling properties for Oracle
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "Min Pool Size";
    public override string? MaxPoolSizeSettingName => "Max Pool Size";

    // Oracle ODP.NET has no "Application Name" connection string key — use "Metadata Pooling"
    // as the pool-key discriminator to guarantee separate pools for reader vs writer connections.
    internal override string? ReadOnlyPoolDiscriminatorSettingName => "Metadata Pooling";
    internal override string? ReadOnlyPoolDiscriminatorSettingValue => "false";

    // TYPE-021 (confirmed live, Oracle Free 23ai full image): SDO_GEOMETRY is an object type ODP.NET reads
    // only through a custom UDT class, so the server converts. A Geometry/Geography is written as EWKT
    // text bound as a CLOB and built by SDO_GEOMETRY(wkt, srid) in a scalar subquery (ODP.NET binds by
    // position, so the marker appears once; SRID 0, pengdows' "unknown", is stored as NULL) and read
    // as EWKT: SDO_UTIL.TO_WKTGEOMETRY plus the SRID from SDO_UTIL.TO_JSON, which needs no table alias.
    // Oracle stores ordinates to 15 significant digits on every path (WKT and WKB alike).
    private static bool IsSpatial(IColumnInfo column)
    {
        var type = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ?? column.PropertyInfo.PropertyType;
        return typeof(types.valueobjects.SpatialValue).IsAssignableFrom(type);
    }

    public override bool RendersColumnArgument(IColumnInfo column) =>
        IsSpatial(column) || base.RendersColumnArgument(column);

    public override string RenderColumnArgument(string parameterMarker, IColumnInfo column) =>
        IsSpatial(column)
            ? "(SELECT CASE WHEN x IS NULL THEN NULL ELSE SDO_GEOMETRY(SUBSTR(x, INSTR(x, ';') + 1), " +
              "NULLIF(TO_NUMBER(SUBSTR(x, 6, INSTR(x, ';') - 6)), 0)) END " +
              $"FROM (SELECT TO_CLOB({parameterMarker}) x FROM DUAL))"
            : base.RenderColumnArgument(parameterMarker, column);

    // A NULL geometry binds as text like a value does; ODP.NET refuses an untyped NULL (ORA-50028),
    // which a NULL interval or TimeSpan declared DbType.Object was too (found live 2026-10-06).
    internal override DbType? NullParameterDbType(IColumnInfo column) =>
        IsSpatial(column) || IsInterval(column) ? DbType.String : base.NullParameterDbType(column);

    // Declared DbType.Object only: a NULL declared DbType.Time already binds as INTERVAL DAY TO SECOND.
    private static bool IsInterval(IColumnInfo column)
    {
        if (column.DbType != DbType.Object)
        {
            return false;
        }

        var type = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ?? column.PropertyInfo.PropertyType;
        return type == typeof(types.valueobjects.IntervalDaySecond) ||
               type == typeof(types.valueobjects.IntervalYearMonth) || type == typeof(TimeSpan);
    }

    // HARN-016, confirmed live (ODP.NET 23): every OracleDataSource owns a private pool, even for an
    // identical connection string, and disposing it leaves the pool's idle connections open until the
    // process exits, so each disposed context kept up to two server sessions. OracleDataSource.
    // ClearPool() closes them and no other data source's (live: 10 contexts, +0 sessions); a connection
    // still checked out keeps working and is closed when it is disposed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.MethodInfo?> ClearPoolMethods = new();

    internal override void ClearOwnedDataSourcePool(System.Data.Common.DbDataSource dataSource)
    {
        var clearPool = ClearPoolMethods.GetOrAdd(dataSource.GetType(),
            static type => type.GetMethod("ClearPool", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                Type.EmptyTypes));
        clearPool?.Invoke(dataSource, null);
    }

    // DRY-028, found live: Oracle rounds the fractional digits a TIMESTAMP(n) or INTERVAL DAY TO SECOND(n)
    // can't hold (one tick before midnight was stored as the next day), and DATE keeps whole seconds.
    // ODP.NET reports the scale in its schema table, and the value is truncated to it.
    internal override bool DeclaredTypeIncludesScale => true;

    internal override bool NeedsDeclaredType(IColumnInfo column, bool forRead) =>
        (!forRead && IsTemporalColumn(column)) || base.NeedsDeclaredType(column, forRead);

    internal override decimal? TemporalUnitsPerMinute(IColumnInfo column) =>
        SplitDeclaredType(DeclaredTypeOf(column)) switch
        {
            ("date", _) => 60,
            ("timestamp" or "timestampltz" or "timestamptz" or "intervalds", var scale) => UnitsPerMinuteForScale(scale),
            _ => null
        };

    // An IntervalDaySecond is bound as Oracle's interval literal (six fractional digits); a column
    // declared with fewer gets only its own, cut, not rounded.
    public override void MarkColumnParameter(DbParameter parameter, IColumnInfo column)
    {
        base.MarkColumnParameter(parameter, column);
        if (parameter.Value is string literal && TemporalUnitsPerMinute(column) != null &&
            SplitDeclaredType(DeclaredTypeOf(column)).Scale is { } digits && literal.IndexOf('.') is var dot and > 0 &&
            literal.Length > dot + 1 + digits)
        {
            parameter.Value = digits == 0 ? literal[..dot] : literal[..(dot + 1 + digits)];
        }
    }

    // ODP.NET 21 (3.21) returns TIMESTAMP WITH TIME ZONE from GetValue as its wall time with no offset;
    // its GetDateTimeOffset returns the value (ODP.NET 23 returns it from GetValue; both confirmed live).
    internal override string? OffsetTimestampDataTypeName => "TimeStampTZ";

    internal override string RenderColumnSelect(string columnReference, string wrappedName, IColumnInfo column) =>
        IsSpatial(column)
            ? $"CASE WHEN {columnReference} IS NULL THEN NULL ELSE 'SRID=' || " +
              $"NVL(JSON_VALUE(SDO_UTIL.TO_JSON({columnReference}), '$.srid'), '0') || ';' || " +
              $"SDO_UTIL.TO_WKTGEOMETRY({columnReference}) END AS {wrappedName}"
            : columnReference;

    // Oracle ODP.NET 23.x throws ArgumentException for DbType.Boolean and DbType.Guid.
    // Remap to safe native types; ApplyGuidFormat then serializes the Guid to VARCHAR2(36).
    // TYPE-002, confirmed live (ODP.NET 23.8): DbType.DateTime2 and DbType.Xml are rejected too ("Value
    // does not fall within the expected range"); DbType.DateTime is TIMESTAMP (all 7 fraction digits)
    // and VARCHAR2 text converts to XMLTYPE.
    protected override DbType RemapDbType(DbType type) => type switch
    {
        DbType.Boolean => DbType.Int16,
        DbType.Guid => DbType.String,
        DbType.DateTime2 => DbType.DateTime,
        DbType.Xml => DbType.String,
        _ => type
    };

    // TYPE-002, confirmed live: ODP.NET binds DbType.Double/Single as NUMBER, so a double beyond
    // NUMBER's 1e126 overflowed on bind. System.Double/Single are IEEE: BINARY_DOUBLE/BINARY_FLOAT,
    // which also store into NUMBER/FLOAT columns and compare with them.
    private static void BindAsBinaryFloatingPoint(DbParameter parameter, DbType type)
    {
        ProviderPropertySetter.Set(parameter, "OracleDbType", type == DbType.Double ? "BinaryDouble" : "BinaryFloat");
    }

    // ODP.NET reads LONG/LONG RAW as empty unless the select list has the row's key or ROWID, or
    // InitialLONGFetchSize is -1 (fetch it all with the row), confirmed live (TYPE-002).
    internal override void ConfigureCommand(DbCommand command)
    {
        ProviderPropertySetter.Set(command, "InitialLONGFetchSize", "-1");
    }

    // Oracle has no TIME type; a time of day binds as INTERVAL DAY TO SECOND (TYPE-001). A value is
    // mapped by AdvancedTypeRegistry; a NULL declared DbType.Time must be too, since ODP.NET turns
    // DbType.Time into OracleDbType.TimeStamp and Oracle type-checks a NULL bind against the column
    // ("ORA-00932: expression is of data type TIMESTAMP", confirmed live).
    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        // TYPE-021: a spatial value is EWKT for RenderColumnArgument's SDO_GEOMETRY(...), bound as a
        // CLOB: as VARCHAR2 a long geometry fails with ORA-01461 (confirmed live).
        if (value is types.valueobjects.SpatialValue spatial)
        {
            var text = base.CreateDbParameter(name, DbType.String, types.converters.ExtendedWellKnownText.From(spatial));
            ProviderPropertySetter.Set(text, "OracleDbType", "Clob");
            return text;
        }

        var parameter = base.CreateDbParameter(name, type, value);
        if (type is DbType.Double or DbType.Single)
        {
            BindAsBinaryFloatingPoint(parameter, type);
        }
        if (type == DbType.Time)
        {
            parameter.DbType = DbType.Object;
            SetOracleIntervalDaySecond(parameter);
        }

        return parameter;
    }

    public override object? PrepareParameterValue(object? value, DbType dbType)
    {
        if (dbType == DbType.Boolean && value is bool b)
        {
            return b ? (short)1 : (short)0;
        }

        return base.PrepareParameterValue(value, dbType);
    }

    public override bool IsUniqueViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) == 1;

    public override bool IsForeignKeyViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) is 2291 or 2292;

    public override bool IsNotNullViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) == 1400;

    public override bool IsCheckConstraintViolation(DbException ex) =>
        TryGetProviderErrorCode(ex) == 2290;

    // ConstraintViolation is deliberately not checked here — SqlDialect.ClassifyException already
    // checks IsUniqueViolation/IsForeignKeyViolation/IsNotNullViolation/IsCheckConstraintViolation
    // before ever calling this method, so a redundant error-code-list re-check here would just be
    // a second, independently-maintained copy of the same "is this a constraint violation" signal.
    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        // ORA-01456 write inside a READ ONLY transaction (live); ORA-16000 database open for
        // read-only access (documented) (REV-050).
        if (TryGetProviderErrorCode(ex) is 1456 or 16000)
        {
            category = DbErrorCategory.ReadOnlyViolation;
            return true;
        }

        var errorCode = TryGetProviderErrorCode(ex);

        if (errorCode == 60)
        {
            category = DbErrorCategory.Deadlock;
            return true;
        }

        // ORA-01466: a read-only or serializable transaction's snapshot predates the table's last
        // DDL (e.g. a read-only read of a table created moments earlier). Oracle's remedy is the
        // same as ORA-08177's: end the transaction and run it again.
        if (errorCode is 8177 or 1466)
        {
            category = DbErrorCategory.SerializationFailure;
            return true;
        }

        category = DbErrorCategory.Unknown;
        return false;
    }

    // Isolation mapping (DEC-010; was IsolationResolver's per-database switch, same names as 3.0).
    internal override HashSet<IsolationLevel> GetSupportedIsolationLevels(bool allowSnapshotIsolation) =>
        new HashSet<IsolationLevel>
        {
            IsolationLevel.ReadCommitted,
            IsolationLevel.Serializable
        };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) =>
        new Dictionary<IsolationProfile, IsolationLevel>
        {
            [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.ReadCommitted,
            [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
            [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
        };

    // ---- Instance-free traits (REV-039): exception translator, value formats, type mappings ----

    // OracleParameter property names, set by reflection (no ODP.NET reference).
    private static class OracleNames
    {
        public const string DbTypeProperty = "OracleDbType";
        public const string IntervalYM = "IntervalYM";
        public const string IntervalDS = "IntervalDS";
        public const string TimeStampTZ = "TimeStampTZ";
        public const string Blob = "Blob";
        public const string Clob = "Clob";
    }

    /// <summary>Marks an ODP.NET parameter as OracleDbType.IntervalDS (no-op for other providers).</summary>
    private static void SetOracleIntervalDaySecond(DbParameter parameter) =>
        AdvancedTypeRegistry.SetEnumProperty(parameter, OracleNames.DbTypeProperty, OracleNames.IntervalDS);

    internal static DatabaseTraits CreateOracleTraits() =>
        new(SupportedDatabase.Oracle, new OracleExceptionTranslator())
        {
            // EWKT text, always with its SRID: RenderColumnArgument builds SDO_GEOMETRY from it (TYPE-021).
            SpatialFormat = SpatialWireFormat.ExtendedWellKnownText,
            IntervalFormat = IntervalWireFormat.OracleLiteral,
            RegisterTypeMappings = RegisterOracleTypeMappings
        };

    private static void RegisterOracleTypeMappings(AdvancedTypeRegistry registry)
    {
        // bool as NUMBER(1) via Int16
        registry.RegisterMapping<bool>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Int16,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.Int16;
                if (value is bool b)
                {
                    param.Value = b ? 1 : 0;
                }
            }
        });

        // Guid: handled by GuidFormat (GuidStorageFormat.String), not a type mapping.

        registry.RegisterMapping<IntervalYearMonth>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                AdvancedTypeRegistry.SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.IntervalYM);
            }
        });

        registry.RegisterMapping<IntervalDaySecond>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                AdvancedTypeRegistry.SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.IntervalDS);
            }
        });

        // Oracle has no TIME type and ODP.NET rejects DbType.Time ("ORA-50028: Invalid parameter
        // binding", confirmed live), so a time of day binds as INTERVAL DAY TO SECOND (TYPE-001).
        // A NULL declared DbType.Time is handled the same way in this dialect.
        registry.RegisterMapping<TimeSpan>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) => SetOracleIntervalDaySecond(param)
        });

        // DateTimeOffset uses TIMESTAMP WITH TIME ZONE (OracleDbType.TimeStampTZ), which keeps the
        // offset, so the value is sent as given (confirmed live, ODP.NET 23.26 and 3.21). A column with
        // no offset is declared DateTime and gets the UTC instant before this mapping is reached.
        registry.RegisterMapping<DateTimeOffset>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
                AdvancedTypeRegistry.SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.TimeStampTZ)
        });

        // BLOB / CLOB
        registry.RegisterMapping<Stream>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) =>
            {
                AdvancedTypeRegistry.SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.Blob);
            }
        });
        registry.RegisterMapping<TextReader>(SupportedDatabase.Oracle, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                AdvancedTypeRegistry.SetEnumProperty(param, OracleNames.DbTypeProperty, OracleNames.Clob);
            }
        });
    }
}
