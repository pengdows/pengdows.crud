// =============================================================================
// FILE: PostgreSqlDialect.cs
// PURPOSE: PostgreSQL specific dialect implementation.
//
// AI SUMMARY:
// - Supports PostgreSQL 10+ with comprehensive SQL standard compliance.
// - Key features:
//   * INSERT ... ON CONFLICT for upserts (supports DO UPDATE and DO NOTHING); MERGE on 15+
//   * Parameter marker: @ (ADO.NET standard; avoids Npgsql '::' cast lookahead)
//   * Identifier quoting: "name" (double quotes)
//   * Max parameters: 32767 (practical limit)
//   * Prepared statements enabled for performance
// - Session settings: standard_conforming_strings, client_min_messages.
// - RETURNING clause for getting generated IDs.
// - Array/range type support via Npgsql.
// - CockroachDB uses this dialect (Postgres-compatible wire protocol).
// - Stored procedure support via CALL statement.
// =============================================================================

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.types.valueobjects;
using pengdows.crud.types;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// PostgreSQL dialect with comprehensive SQL standard compliance.
/// </summary>
/// <remarks>
/// <para>
/// Supports PostgreSQL 10 and later with automatic version detection.
/// Also used for CockroachDB (Postgres-compatible).
/// </para>
/// <para>
/// <strong>UPSERT:</strong> Uses INSERT ... ON CONFLICT (key) DO UPDATE, or MERGE on PostgreSQL 15+.
/// </para>
/// <para>
/// <strong>Prepared Statements:</strong> Enabled by default for performance.
/// </para>
/// </remarks>
internal class PostgreSqlDialect : SqlDialect
{
    internal override bool EnforcesReadOnlyTransactions => true;

    private const string StandardConformingStringsSetting = "standard_conforming_strings";
    private const string ClientMinMessagesSetting = "client_min_messages";
    private const string ReadOnlyTransactionSetting = "default_transaction_read_only";

    // Connection string keys used when baking startup options
    private const string NpgsqlOptionsKey = "Options";

    private bool _settingsBaked;
    internal override bool SessionSettingsBakedIntoDataSource => _settingsBaked;

    // Reflection-based property/type names used to stamp NpgsqlDbType on JSON parameters
    private const string DataTypeNameProperty = "DataTypeName";
    private const string NpgsqlDbTypeProperty = "NpgsqlDbType";
    private const string JsonbTypeName = "Jsonb";

    private const string DefaultSessionSettings =
        $"SET {StandardConformingStringsSetting} = on, {ClientMinMessagesSetting} = warning;";

    private static readonly IReadOnlyDictionary<string, string> ExpectedSessionSettings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [StandardConformingStringsSetting] = "on",
            [ClientMinMessagesSetting] = "warning"
        };

    private string? _sessionSettings;
    private readonly SupportedDatabase _flavor;

    internal PostgreSqlDialect(DbProviderFactory factory, ILogger logger, SupportedDatabase flavor = SupportedDatabase.PostgreSql)
        : base(factory, logger)
    {
        _flavor = flavor;
    }

    public override SupportedDatabase DatabaseType => _flavor;

    // TYPE-003, confirmed live 2026-09-30: the provider rejects DbType.SByte and the unsigned DbTypes.
    internal override bool BindsSByteAndUnsignedNatively => false;

    // Aurora PostgreSQL is a detection label for the PostgreSQL engine (VAR-001): same type mappings.
    internal override SupportedDatabase TypeMappingProvider =>
        DatabaseType == SupportedDatabase.AuroraPostgreSql ? SupportedDatabase.PostgreSql : DatabaseType;

    // Use '@' parameter marker — ADO.NET standard; avoids Npgsql's '::' cast lookahead
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;

    protected override bool NormalizeDateTimeOffsetToUtc => true;

    public override bool SupportsSetValuedParameters => true;

    // A cached parameter that was created for a scalar keeps Npgsql's scalar NpgsqlDbType /
    // DataTypeName (e.g. 'bigint') after its value becomes an array, and Npgsql 9 rejects that:
    // "Writing values of 'System.Int64[]' is not supported for parameters having DataTypeName
    // 'bigint'" (confirmed live). Retype it as Array | element.
    // Npgsql maps DbType.DateTime2 to timestamp without time zone and refuses a DateTime with
    // Kind=Utc for it. pengdows stores UTC wall time there, so the same instant goes out with
    // Kind=Unspecified (TYPE-002). DbType.DateTime is timestamptz in Npgsql and keeps Kind=Utc.
    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        if (type == DbType.DateTime2 && value is DateTime { Kind: DateTimeKind.Utc } utc)
        {
            return base.CreateDbParameter(name, type, DateTime.SpecifyKind(utc, DateTimeKind.Unspecified));
        }

        // TYPE-002: a DateTimeOffset declared DbType.Time is a time with an offset (timetz), sent with
        // its offset; as timestamptz, "WHERE v = @p" on a timetz column has no operator (confirmed live).
        if (type == DbType.Time && value is DateTimeOffset timeWithOffset)
        {
            var timetz = base.CreateDbParameter<object?>(name, DbType.Object, null);
            timetz.Value = timeWithOffset;
            SetNpgsqlParameterType(timetz, "TimeTz", "time with time zone");
            return timetz;
        }

        // TYPE-020: a C# enum written by name in your own SQL ("WHERE mood = {P}m") must reach a
        // user-defined ENUM column as the column's type, so it goes untyped, as the gateways send it.
        // Npgsql can't write a CLR enum at all ("Writing values of '...' is not supported", confirmed live),
        // so its name goes; CockroachDB takes it as text.
        if (value is Enum enumValue && type is DbType.String or DbType.AnsiString or DbType.StringFixedLength
                or DbType.AnsiStringFixedLength)
        {
            var enumName = base.CreateDbParameter<object?>(name, type, enumValue.ToString());
            if (SendsEnumParametersUntyped)
            {
                SetNpgsqlDbTypeOnly(enumName, "Unknown");
            }

            return enumName;
        }

        // TYPE-002, confirmed live (Npgsql 9): Npgsql can't infer pg_lsn from an
        // NpgsqlLogSequenceNumber and refuses the parameter unless the type is named.
        if (value is not null && value.GetType().FullName == "NpgsqlTypes.NpgsqlLogSequenceNumber")
        {
            var lsn = base.CreateDbParameter(name, type, value);
            SetNpgsqlParameterType(lsn, "PgLsn", "pg_lsn");
            return lsn;
        }

        return base.CreateDbParameter(name, type, value);
    }

    internal override void ConfigureSetValuedParameter(DbParameter parameter, Array value)
    {
        var property = parameter.GetType().GetProperty(NpgsqlDbTypeProperty);
        if (property == null || !property.PropertyType.IsEnum)
        {
            return;
        }

        var elementType = value.GetType().GetElementType();
        var elementName = elementType == typeof(long) ? "Bigint"
            : elementType == typeof(int) ? "Integer"
            : elementType == typeof(short) ? "Smallint"
            : elementType == typeof(string) ? "Text"
            : elementType == typeof(Guid) ? "Uuid"
            : elementType == typeof(bool) ? "Boolean"
            : null;

        if (elementName == null)
        {
            // Any other element type was typed integer[] (REV-065). Clear the stale scalar type
            // instead and let Npgsql infer the array type from the value.
            parameter.ResetDbType();
            parameter.GetType().GetProperty(DataTypeNameProperty)?.SetValue(parameter, null);
            return;
        }

        try
        {
            var element = Enum.Parse(property.PropertyType, elementName, ignoreCase: true);
            var array = Enum.Parse(property.PropertyType, "Array", ignoreCase: true);
            var combined = Enum.ToObject(
                property.PropertyType,
                Convert.ToInt64(element) | Convert.ToInt64(array));
            property.SetValue(parameter, combined);
            parameter.GetType().GetProperty(DataTypeNameProperty)?.SetValue(
                parameter,
                elementName.ToLowerInvariant() + "[]");
        }
        catch (ArgumentException)
        {
            // Non-Npgsql parameters and provider versions without a matching enum member use
            // their normal inference path.
        }
    }

    public override bool SupportsBatchUpdate => true;

    // TYPE-020 / WRT-007, confirmed live (PostgreSQL 16.4, CockroachDB 25.1, YugabyteDB 2025.2): a VALUES
    // source types a C# enum's name as text (untyped, or text on CockroachDB), which a user-defined
    // ENUM column refuses. An empty SELECT of the target's own columns followed by one SELECT per row,
    // UNION ALL, gives each value its column's type instead, with no type name needed. Only for rows
    // holding such an enum: a long UNION ALL plans more slowly than one VALUES list.
    private bool HasEnumStoredByName(IReadOnlyList<IColumnInfo> columns)
    {
        foreach (var column in columns)
        {
            if ((column.IsEnum && column.EnumAsString) || IsStringIntoNonTextColumn(column))
            {
                return true;
            }
        }

        return false;
    }

    // TYPE-020: a string property bound to a column whose declared type isn't text (a user-defined
    // ENUM, uuid, ...) is written like a C# enum stored by name: untyped where the dialect does that,
    // and through the table-typed source in MERGE/batch update. Learned from the declared types.
    private static bool IsStringProperty(IColumnInfo column) =>
        !column.IsJsonType && !column.IsEnum &&
        (Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ?? column.PropertyInfo.PropertyType) == typeof(string);

    internal override bool NeedsDeclaredType(IColumnInfo column, bool forRead) => !forRead && IsStringProperty(column);

    private bool IsStringIntoNonTextColumn(IColumnInfo column) =>
        IsStringProperty(column) && DeclaredTypeOf(column) is { } declared && !IsTextType(declared);

    private static bool IsTextType(string declared)
    {
        var name = declared.Contains('.') ? declared[(declared.LastIndexOf('.') + 1)..] : declared;
        return name.StartsWith("text", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("character", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("varchar", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("bpchar", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("char", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("string", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendTypedSourceTemplate(ISqlQueryBuilder query, string tableName, IReadOnlyList<string> columns)
    {
        query.Append("SELECT ");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(columns[i]);
        }

        query.Append(" FROM ").Append(tableName).Append(" WHERE FALSE");
    }

    internal override string RenderMergeSource(IReadOnlyList<IColumnInfo> columns, IReadOnlyList<string> parameterNames,
        string tableName)
    {
        if (!HasEnumStoredByName(columns))
        {
            return RenderMergeSource(columns, parameterNames);
        }

        var names = new List<string>(columns.Count);
        var values = new System.Text.StringBuilder();
        for (var i = 0; i < columns.Count; i++)
        {
            names.Add(WrapObjectName(columns[i].Name));
            if (i > 0)
            {
                values.Append(", ");
            }

            var placeholder = MakeParameterName(parameterNames[i]);
            values.Append(RendersColumnArgument(columns[i]) ? RenderColumnArgument(placeholder, columns[i]) : placeholder);
        }

        var source = new SqlQueryBuilder();
        AppendTypedSourceTemplate(source, tableName, names);
        return string.Concat("USING (", source.ToString(), " UNION ALL SELECT ", values.ToString(), ") AS s (",
            string.Join(", ", names), ")");
    }

    internal override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue,
        IReadOnlyList<IColumnInfo> columns)
    {
        if (rowCount <= 0 || !HasEnumStoredByName(columns))
        {
            BuildBatchUpdateSql(tableName, columnNames, keyColumns, rowCount, query, getValue);
            return;
        }

        query.Append("UPDATE ").Append(tableName).Append(" AS t SET ");
        for (var i = 0; i < columnNames.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(columnNames[i]).Append(" = s.").Append(columnNames[i]);
        }

        var allCols = new List<string>(keyColumns);
        allCols.AddRange(columnNames);
        query.Append(" FROM (");
        AppendTypedSourceTemplate(query, tableName, allCols);
        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            query.Append(" UNION ALL SELECT ");
            for (var col = 0; col < allCols.Count; col++)
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
                    query.Append(ParameterMarker).Append('b')
                        .Append(paramIdx++.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        query.Append(") AS s(").Append(string.Join(", ", allCols)).Append(") WHERE ");
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (i > 0)
            {
                query.Append(" AND ");
            }

            query.Append("t.").Append(keyColumns[i]).Append(" = s.").Append(keyColumns[i]);
        }
    }

    /// <inheritdoc />
    public override void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        if (rowCount <= 0)
        {
            return;
        }

        // PostgreSQL UPDATE FROM VALUES pattern:
        // UPDATE target SET col1 = s.col1, ...
        // FROM (VALUES (@b0, @b1), (@b2, @b3)) AS s(pk, col1)
        // WHERE target.pk = s.pk

        query.Append("UPDATE ");
        query.Append(tableName);
        query.Append(" AS t SET ");

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

        query.Append(" FROM (VALUES ");

        var allCols = new List<string>(keyColumns);
        allCols.AddRange(columnNames);

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            if (row > 0)
            {
                query.Append(", ");
            }

            query.Append('(');
            for (var col = 0; col < allCols.Count; col++)
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

            query.Append(')');
        }

        query.Append(") AS s(");
        for (var i = 0; i < allCols.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(allCols[i]);
        }

        query.Append(") WHERE ");
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
    }

    // IMMUTABLE: PostgreSQL practical parameter limit - do not change without extensive testing
    public override int MaxParameterLimit => 32767;

    // IMMUTABLE: PostgreSQL conservative output parameter limit for stored functions - do not change without extensive testing
    public override int MaxOutputParameters => 100;

    // IMMUTABLE: PostgreSQL NAMEDATALEN-1 identifier limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 63;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.PostgreSQL;
    public override bool RequiresStoredProcParameterNameMatch => true;

    // PostgreSQL benefits from prepared statements
    public override bool PrepareStatements => true;
    public override bool SupportsReadOnlyTransactions => true;

    [Obsolete("MaxSupportedStandard is a coarse heuristic; query the specific Supports* capability instead.", false)]
    public override SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? base.MaxSupportedStandard : DetermineStandardCompliance(null);

    public override bool SupportsNamespaces => true;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsOnConflictWhere => true; // Supports WHERE predicate on DO UPDATE (9.5+)
    public override bool SupportsMerge => DatabaseType != SupportedDatabase.CockroachDb && IsVersionAtLeast(15);

    // Identity columns and OVERRIDING SYSTEM VALUE arrived in PostgreSQL 10. YugabyteDB (YSQL,
    // PG 11+) supports the clause and inherits this; CockroachDbDialect overrides it to false
    // (it rejects the clause as a syntax error, cockroachdb/cockroach#68201).
    internal override bool SupportsOverridingSystemValue =>
        !IsInitialized || ProductInfo.ParsedVersion == null || IsVersionAtLeast(10);
    public override bool SupportsSavepoints => true;
    public override bool SupportsJsonTypes => IsVersionAtLeast(9);
    public override bool SupportsSqlJsonConstructors => IsVersionAtLeast(18);
    public override bool SupportsJsonTable => IsVersionAtLeast(18);
    public override bool SupportsMergeReturning => IsVersionAtLeast(18);
    // PostgreSQL MERGE does NOT allow table alias on left side of UPDATE SET
    // Correct: UPDATE SET col = value
    // Error:   UPDATE SET t.col = value  -- "column 't' of relation 'table' does not exist"
    public override bool MergeUpdateRequiresTargetAlias => false;
    public override bool SupportsInsertReturning => true;

    // Inherited by AuroraPostgreSql (this class with a flavor), Spanner, CockroachDb and
    // YugabyteDb — all support RETURNING with the same syntax. The base switch keys on
    // DatabaseType and has no AuroraPostgreSql/Spanner arm, which dropped RETURNING for them.
    public override string RenderInsertReturningClause(string idColumnWrapped) =>
        $" RETURNING {idColumnWrapped}";

    public override string GetLastInsertedIdQuery()
    {
        // Fallback method - prefer RETURNING clause
        return "SELECT lastval()";
    }

    public override string RenderJsonArgument(string parameterMarker, IColumnInfo column)
    {
        return string.Concat(parameterMarker, "::jsonb");
    }

    // TYPE-002, confirmed live (PostgreSQL 17): a C# enum's name sent as a text parameter is refused
    // by a user-defined ENUM column ("column is of type mood but expression is of type text").
    // Untyped, the server applies the column's type, so the same property also writes to a text
    // column. Known gap: a MERGE upsert's VALUES list still types it as text (3.0 will name the type).
    // TYPE-002, confirmed live (PostGIS 3.5, CockroachDB 25.1, Npgsql 9): with no NetTopologySuite or
    // pgvector plugin, Npgsql has no handler for geometry/geography/vector and GetFieldType/GetValue
    // throw, but GetBytes returns the binary wire value: EWKB, or pgvector's format.
    internal override bool ReadsUnresolvedColumns => true;

    internal override Type? GetUnresolvedColumnType(string dataTypeName)
    {
        var name = dataTypeName.Contains('.') ? dataTypeName[(dataTypeName.LastIndexOf('.') + 1)..] : dataTypeName;
        return name.ToLowerInvariant() switch
        {
            "geometry" => typeof(types.valueobjects.Geometry),
            "geography" => typeof(types.valueobjects.Geography),
            "vector" => typeof(float[]),
            _ => null
        };
    }

    internal override object ReadUnresolvedColumn(IDataRecord record, int ordinal, Type type)
    {
        var bytes = UnresolvedColumnReader.ReadBytes(record, ordinal);
        if (type == typeof(float[]))
        {
            // pgvector's binary format: int16 dimensions, int16 unused, then big-endian float4s.
            var dimensions = System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(bytes);
            var vector = new float[dimensions];
            for (var i = 0; i < dimensions; i++)
            {
                vector[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(4 + 4 * i));
            }

            return vector;
        }

        if (type == typeof(types.valueobjects.Geometry) || type == typeof(types.valueobjects.Geography))
        {
            types.converters.GeometryConverter.ExtractSridFromEwkb(bytes, out var srid, out var wkb);
            return type == typeof(types.valueobjects.Geography)
                ? types.valueobjects.Geography.FromWellKnownBinary(wkb, srid == 0 ? 4326 : srid)
                : types.valueobjects.Geometry.FromWellKnownBinary(wkb, srid);
        }

        return base.ReadUnresolvedColumn(record, ordinal, type);
    }

    public override void MarkColumnParameter(DbParameter parameter, IColumnInfo column)
    {
        base.MarkColumnParameter(parameter, column);
        if (((column.IsEnum && column.EnumAsString) || IsStringIntoNonTextColumn(column)) && SendsEnumParametersUntyped)
        {
            SetNpgsqlDbTypeOnly(parameter, "Unknown");
        }
    }

    /// <summary>
    /// True when a C# enum's name is sent untyped so a user-defined ENUM column accepts it.
    /// CockroachDB assigns text to an ENUM itself and refuses an untyped value in a VALUES list.
    /// </summary>
    internal virtual bool SendsEnumParametersUntyped => true;

    internal override bool MarksColumnParameter(IColumnInfo column) =>
        IsStringIntoNonTextColumn(column) || base.MarksColumnParameter(column);

    private protected static void SetNpgsqlDbTypeOnly(DbParameter parameter, string npgsqlDbTypeName)
    {
        ProviderPropertySetter.Set(parameter, NpgsqlDbTypeProperty, npgsqlDbTypeName);
    }

    public override void TryMarkJsonParameter(DbParameter parameter, IColumnInfo column)
    {
        base.TryMarkJsonParameter(parameter, column);
        parameter.DbType = DbType.String;
        parameter.Size = 0;

        try
        {
            ProviderPropertySetter.Set(parameter, DataTypeNameProperty, "jsonb");
            ProviderPropertySetter.Set(parameter, NpgsqlDbTypeProperty, JsonbTypeName);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to stamp NpgsqlDbType metadata for JSON parameter {Parameter}.",
                parameter.ParameterName);
        }
    }

    public override string GetVersionQuery()
    {
        return "SELECT version()";
    }

    public override Version? ParseVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        var match = Regex.Match(versionString, @"PostgreSQL\s+(\d+(?:\.\d+)*)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var raw = match.Groups[1].Value;
            var normalized = raw.Contains('.', StringComparison.Ordinal) ? raw : raw + ".0";
            if (Version.TryParse(normalized, out var version))
            {
                return version;
            }
        }

        return base.ParseVersion(versionString);
    }

    internal override async Task<string?> GetProductNameCoreAsync(ITrackedConnection connection, bool useAsync)
    {
        var name = await base.GetProductNameCoreAsync(connection, useAsync).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(name) && name!.IndexOf("npgsql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "PostgreSQL";
        }

        return name;
    }

    internal override async Task<IDatabaseProductInfo> DetectDatabaseInfoCoreAsync(ITrackedConnection connection, bool useAsync)
    {
        var productInfo = await base.DetectDatabaseInfoCoreAsync(connection, useAsync);

        // Check and cache PostgreSQL session settings during initialization
        if (_sessionSettings == null)
        {
            var result = GetPostgreSqlSessionSettings(connection);
            _sessionSettings = result.Settings;

            var snapshot = string.Join(", ", result.Snapshot.Select(kv => $"{kv.Key}={kv.Value}"));
            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation(
                    "PostgreSQL session settings detected: {CurrentSettings}. Applying changes:\n{Settings}", snapshot,
                    _sessionSettings);
            }
            else
            {
                Logger.LogInformation(
                    "PostgreSQL session settings detected: {CurrentSettings}. Already compliant; enforcing baseline on every checkout",
                    snapshot);
            }
        }

        return productInfo;
    }

    public override string GetFinalSessionSettings(bool readOnly)
    {
        // 1 RTT / 1 Command Optimization: Consolidate all session assignments (baseline + intent)
        // into a single comma-separated SET command.
        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            sb.Append("SET ");
            sb.Append(StandardConformingStringsSetting);
            sb.Append(" = on, ");
            sb.Append(ClientMinMessagesSetting);
            sb.Append(" = warning, ");
            sb.Append(ReadOnlyTransactionSetting);
            sb.Append(readOnly ? " = on;" : " = off;");

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    private SessionSettingsResult GetPostgreSqlSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                // Always set the full baseline to ensure deterministic state in pooled connections.
                return new SessionSettingsResult(DefaultSessionSettings, ExpectedSessionSettings, false);
            },
            () => new SessionSettingsResult(
                DefaultSessionSettings,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [StandardConformingStringsSetting] = "unknown",
                    [ClientMinMessagesSetting] = "unknown"
                },
                true),
            "Failed to configure PostgreSQL session settings");
    }

    public override string GetBaseSessionSettings()
    {
        // Always enforce the full baseline on every connection checkout.
        // A cached empty diff means the first sampled connection was already compliant,
        // but pooled connections can drift if external code mutates session state.
        return string.IsNullOrWhiteSpace(_sessionSettings) ? DefaultSessionSettings : _sessionSettings;
    }

    public override string GetReadOnlySessionSettings()
    {
        return $"SET {ReadOnlyTransactionSetting} = on;";
    }

    internal override string? GetReadOnlyTransactionResetSql()
    {
        return $"SET {ReadOnlyTransactionSetting} = off;";
    }

    private static readonly Regex TypeCatalogDdl = new(
        @"^\s*(CREATE|ALTER|DROP)\s+(OR\s+REPLACE\s+)?(EXTENSION|TYPE|DOMAIN)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // CONFIRMED live: Npgsql caches the type catalog per data source, so after CREATE EXTENSION
    // hstore a data source loaded earlier cannot read hstore ("DataTypeName '-'"). Inherited by
    // YugabyteDB/CockroachDB/Spanner, which also run on Npgsql.
    internal override bool InvalidatesProviderTypeCache(string sql) => TypeCatalogDdl.IsMatch(sql);

    public override string? GetReadOnlyConnectionParameter()
    {
        return $"Options='-c {ReadOnlyTransactionSetting}=on'";
    }

    // A connection string keeps only the last value of a repeated key, so appending a second
    // Options= would silently drop the caller's own startup options (e.g. -c lock_timeout=5s)
    // from every read connection. Merge the read-only setting into the caller's Options instead.
    protected override string BuildReadOnlyConnectionString(string connectionString, string readOnlyParameter)
    {
        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            // Not a key/value connection string: nothing to merge into.
            return base.BuildReadOnlyConnectionString(connectionString, readOnlyParameter);
        }

        var existing = builder.TryGetValue(NpgsqlOptionsKey, out var value) ? value as string : null;
        if (string.IsNullOrWhiteSpace(existing))
        {
            return base.BuildReadOnlyConnectionString(connectionString, readOnlyParameter);
        }

        // The writer's baked Options may already carry default_transaction_read_only=off; a read
        // connection must end up with =on regardless of what was there.
        var readOnlyPattern = new Regex($@"{ReadOnlyTransactionSetting}\s*=\s*[^\s']+", RegexOptions.IgnoreCase);
        builder[NpgsqlOptionsKey] = readOnlyPattern.IsMatch(existing)
            ? readOnlyPattern.Replace(existing, $"{ReadOnlyTransactionSetting}=on")
            : $"{existing} -c {ReadOnlyTransactionSetting}=on";

        return builder.ConnectionString;
    }

    /// <summary>
    /// Bakes Npgsql-specific settings (auto-prepare, multiplexing) into the connection
    /// string before the DataSource is created.  Must be called while the connection
    /// string still carries credentials; NpgsqlConnectionStringBuilder preserves them on
    /// round-trip, unlike connection.ConnectionString on a DataSource-created connection.
    /// </summary>
    internal override string PrepareConnectionStringForDataSource(string connectionString, bool readOnly = false)
    {
        try
        {
            ConnectionStringBuilder.ConnectionString = connectionString;
            var builder = ConnectionStringBuilder;
            var modified = false;

            if (!builder.ContainsKey("MaxAutoPrepare") || (int)builder["MaxAutoPrepare"] == 0)
            {
                builder["MaxAutoPrepare"] = 64;
                modified = true;
            }

            if (!builder.ContainsKey("AutoPrepareMinUsages") || (int)builder["AutoPrepareMinUsages"] == 0)
            {
                builder["AutoPrepareMinUsages"] = 2;
                modified = true;
            }

            if (!builder.ContainsKey("Multiplexing") || (bool)builder["Multiplexing"])
            {
                builder["Multiplexing"] = false;
                modified = true;
            }

            // PERFORMANCE: Bake session settings into the PostgreSQL startup Options parameter.
            // Values sent via the protocol startup message become GUC session defaults.
            // PostgreSQL's RESET ALL (sent by Npgsql on pool return) restores parameters to
            // their session defaults — i.e., back to these startup values — so the next
            // checkout requires zero additional SET round-trips.
            var existingOptions = builder.ContainsKey(NpgsqlOptionsKey)
                ? builder[NpgsqlOptionsKey] as string ?? string.Empty
                : string.Empty;
            var mergedOptions = MergeStartupOptions(existingOptions, readOnly);
            if (!string.Equals(existingOptions, mergedOptions, StringComparison.Ordinal))
            {
                builder[NpgsqlOptionsKey] = mergedOptions;
                modified = true;
            }

            if (modified)
            {
                _settingsBaked = true;
                return builder.ConnectionString;
            }

            // Options were already fully baked (user set all required keys to correct values)
            _settingsBaked = true;
            return connectionString;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to prepare connection string for DataSource.");
            _settingsBaked = false;
            return connectionString;
        }
    }

    /// <summary>
    /// Override in subclasses to include additional GUC settings that should be baked
    /// into the PostgreSQL startup <c>Options</c> parameter alongside the three base keys.
    /// The base implementation returns an empty sequence.
    /// </summary>
    protected virtual IEnumerable<(string Key, string Value)> GetAdditionalStartupOptions(bool readOnly)
        => [];

    /// <summary>
    /// Merges the required GUC session settings into the existing PostgreSQL
    /// <c>options</c> startup string.  Format: <c>-c key=value -c key=value …</c>.
    /// User-supplied options for other keys are preserved; our keys are
    /// always overridden to ensure deterministic startup state.
    /// </summary>
    /// <remarks>
    /// SECURITY: <paramref name="existing"/> must come from a trusted source (the
    /// connection string provided by the application).  This method does not
    /// sanitize arbitrary user-supplied values; never pass end-user input here.
    /// </remarks>
    private string MergeStartupOptions(string existing, bool readOnly)
    {
        // Parse existing "-c key=value" tokens into an ordered list so we can
        // detect and replace our specific keys while preserving user-supplied ones.
        var tokens = new List<(string Key, string Value)>();
        var rest = existing.AsSpan().Trim();
        while (!rest.IsEmpty)
        {
            // Expect token to start with "-c " (possibly after whitespace)
            if (rest.StartsWith("-c ", StringComparison.Ordinal) ||
                rest.StartsWith("-c\t", StringComparison.Ordinal))
            {
                rest = rest[3..].TrimStart();
            }
            else
            {
                // Malformed token — skip to next '-c'
                var next = rest.IndexOf("-c ", StringComparison.Ordinal);
                if (next < 0)
                {
                    break;
                }
                rest = rest[next..];
                continue;
            }

            var eqIdx = rest.IndexOf('=');
            if (eqIdx <= 0)
            {
                break;
            }
            var key = rest[..eqIdx].Trim().ToString();
            rest = rest[(eqIdx + 1)..];

            // Value ends at next " -c" or end of string
            var nextFlag = rest.IndexOf(" -c", StringComparison.Ordinal);
            string value;
            if (nextFlag >= 0)
            {
                value = rest[..nextFlag].Trim().ToString();
                rest = rest[nextFlag..].TrimStart();
            }
            else
            {
                value = rest.Trim().ToString();
                rest = default;
            }

            tokens.Add((key, value));
        }

        // Required settings are pooled-connection-hygiene invariants and always win over anything
        // the caller supplied for the same key.
        var requiredKeys = new List<(string Key, string Value)>
        {
            (StandardConformingStringsSetting, "on"),
            (ClientMinMessagesSetting, "warning"),
            (ReadOnlyTransactionSetting, readOnly ? "on" : "off")
        };

        // Dialect-specific additional options (e.g. CockroachDB's lock_timeout) are safety
        // DEFAULTS the caller owns: only fill them in when the caller hasn't set that key, never
        // overwrite an explicit value (BP-118, 3.0 c58cb96).
        foreach (var (ourKey, ourValue) in GetAdditionalStartupOptions(readOnly))
        {
            var alreadyPresent = false;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i].Key, ourKey, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyPresent = true;
                    break;
                }
            }

            if (!alreadyPresent)
            {
                tokens.Add((ourKey, ourValue));
            }
        }

        foreach (var (ourKey, ourValue) in requiredKeys)
        {
            var found = false;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i].Key, ourKey, StringComparison.OrdinalIgnoreCase))
                {
                    tokens[i] = (tokens[i].Key, ourValue);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                tokens.Add((ourKey, ourValue));
            }
        }

        return string.Join(" ", tokens.Select(t => $"-c {t.Key}={t.Value}"));
    }

    public override void ConfigureProviderSpecificSettings(IDbConnection connection, IDatabaseContext context,
        bool readOnly)
    {
        // Npgsql: all provider-specific settings are baked into the connection string
        // before DataSource creation (via PrepareConnectionStringForDataSource).
        // Do NOT read/write connection.ConnectionString here — on DataSource-created
        // connections Npgsql strips credentials (PersistSecurityInfo behaviour) and
        // writing it back would silently drop the password.
        if (connection.GetType().FullName?.StartsWith("Npgsql.") == true)
        {
            return;
        }

        // For non-Npgsql connections in tests, normalize case so assertions using lower-case substrings succeed
        try
        {
            var typeName = connection.GetType().Name;
            var isTestConn = string.Equals(typeName, "TestConnection", StringComparison.Ordinal);
            var isFakeDb = connection.GetType().FullName?.Contains("fakeDb.") == true;
            if (isTestConn && !string.IsNullOrEmpty(connection.ConnectionString) && !isFakeDb)
            {
                connection.ConnectionString = connection.ConnectionString.ToLowerInvariant();
            }
        }
        catch
        {
            /* ignore */
        }
    }

    public override Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>
        {
            { 15, SqlStandardLevel.Sql2016 },
            { 13, SqlStandardLevel.Sql2011 },
            { 11, SqlStandardLevel.Sql2008 },
            { 9, SqlStandardLevel.Sql2003 },
            { 8, SqlStandardLevel.Sql92 }
        };
    }

    public override SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql2008;
    }

    public override string UpsertIncomingColumn(string columnName)
    {
        return $"EXCLUDED.{WrapObjectName(columnName)}";
    }

    public override object? PrepareParameterValue(object? value, DbType dbType)
    {
        if (value is DateTimeOffset dto)
        {
            // Npgsql 6+ requires DateTimeOffset to be UTC when writing to timestamptz.
            return dto.UtcDateTime;
        }

        return base.PrepareParameterValue(value, dbType);
    }

    /// <summary>
    /// Sets Npgsql-specific type properties on a parameter via reflection so that
    /// subclasses can reuse the same logic without duplicating it.
    /// Silently ignores failures when the parameter is not an Npgsql parameter type.
    /// </summary>
    protected void SetNpgsqlParameterType(DbParameter parameter, string npgsqlDbTypeName, string dataTypeName)
    {
        try
        {
            var type = parameter.GetType();
            var npgsqlDbTypeProp = type.GetProperty(NpgsqlDbTypeProperty);
            if (npgsqlDbTypeProp != null)
            {
                if (Enum.TryParse(npgsqlDbTypeProp.PropertyType, npgsqlDbTypeName, true, out var enumVal))
                {
                    npgsqlDbTypeProp.SetValue(parameter, enumVal);
                }
            }

            type.GetProperty(DataTypeNameProperty)?.SetValue(parameter, dataTypeName);
        }
        catch
        {
            // Not an Npgsql parameter or the property is absent — ignore.
        }
    }

    // Tests access a protected member via reflection; provide a protected facade that
    // delegates to the public base implementation without changing API surface.
    protected new SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        return base.DetermineStandardCompliance(version);
    }

    // Connection pooling properties for PostgreSQL (Npgsql)
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "Minimum Pool Size";
    public override string? MaxPoolSizeSettingName => "Maximum Pool Size";
    public override string? ApplicationNameSettingName => "Application Name";

    // Inherited by Spanner/CockroachDb/YugabyteDb/AuroraPostgreSql — all share Postgres's SQLSTATE
    // codes for these constraint-violation categories, and none override them independently.
    public override bool IsUniqueViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23505", StringComparison.OrdinalIgnoreCase);

    public override bool IsForeignKeyViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23503", StringComparison.OrdinalIgnoreCase);

    public override bool IsNotNullViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23502", StringComparison.OrdinalIgnoreCase);

    public override bool IsCheckConstraintViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23514", StringComparison.OrdinalIgnoreCase);

    // Inherited by Spanner/CockroachDb/YugabyteDb/AuroraPostgreSql — all share Postgres's SQLSTATE
    // codes here, and none override this independently.
    protected override bool TryClassifyProviderException(DbException ex, out DbErrorCategory category)
    {
        // 25006 read_only_sql_transaction: a write in a read-only transaction or on a read-only
        // replica (live: PostgreSQL, CockroachDB, YugabyteDB) (REV-050).
        if (string.Equals(TryGetProviderSqlState(ex), "25006", StringComparison.Ordinal))
        {
            category = DbErrorCategory.ReadOnlyViolation;
            return true;
        }

        var sqlState = TryGetProviderSqlState(ex);

        if (string.Equals(sqlState, "40P01", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.Deadlock;
            return true;
        }

        if (string.Equals(sqlState, "40001", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.SerializationFailure;
            return true;
        }

        // 40003 (statement_completion_unknown): standard SQL/PostgreSQL-catalog code that vanilla
        // PostgreSQL defines but never actually raises (no ereport() call anywhere in its source)
        // -- it is CockroachDB's real, documented "result is ambiguous" error, emitted when its
        // distributed consensus layer loses track of a commit's outcome during a network
        // partition/node failure under contention. Kept in this shared override (not carved out
        // to CockroachDbDialect alone) because all four databases here go through the same Npgsql
        // driver and the branch is simply inert -- not wrong -- for PostgreSql/AuroraPostgreSql,
        // which never trigger it; YugabyteDb is architecturally similar to CockroachDb
        // (distributed consensus) and may.
        if (string.Equals(sqlState, "40003", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.AmbiguousResult;
            return true;
        }

        if (string.Equals(sqlState, "55P03", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sqlState, "57014", StringComparison.OrdinalIgnoreCase))
        {
            category = DbErrorCategory.Timeout;
            return true;
        }

        // Checked as a generic category-level fallback, distinct from IsUniqueViolation/
        // IsForeignKeyViolation/IsNotNullViolation/IsCheckConstraintViolation (already checked
        // earlier in SqlDialect.ClassifyException, before this method is ever called) — those four
        // each match one exact SqlState (23505/23503/23502/23514), so a different or generic
        // class-23 SqlState (e.g. bare "23000", or "23P01" exclusion_violation) matches none of
        // them individually but is still, generically, a constraint violation.
        if (!string.IsNullOrWhiteSpace(sqlState) && sqlState.StartsWith("23", StringComparison.Ordinal))
        {
            category = DbErrorCategory.ConstraintViolation;
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
            IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable
        };

    internal override Dictionary<IsolationProfile, IsolationLevel> GetIsolationProfileMapping(bool allowSnapshotIsolation) =>
        new Dictionary<IsolationProfile, IsolationLevel>
        {
            // MVCC RepeatableRead is a transaction-wide snapshot: reads never block on writers
            // and never see non-repeatable reads, which is what the profile promises.
            [IsolationProfile.SafeNonBlockingReads] = IsolationLevel.RepeatableRead,
            [IsolationProfile.StrictConsistency] = IsolationLevel.Serializable,
            [IsolationProfile.FastWithRisks] = IsolationLevel.ReadCommitted
        };

    // ---- Instance-free traits (REV-039): exception translator, value formats, type mappings ----

    // One translator for every PostgreSQL-wire database (PostgreSQL, Aurora PostgreSQL,
    // CockroachDB, YugabyteDB, Spanner).
    private protected static readonly IDbExceptionTranslator PostgreSqlFamilyExceptionTranslator =
        new PostgresExceptionTranslator();

    // NpgsqlParameter property names, set by reflection (no Npgsql reference). Typos here fail
    // silently at runtime; centralising makes them grep-able.
    private static class NpgsqlNames
    {
        public const string DbTypeProperty = "NpgsqlDbType";
        public const string DataTypeName = "DataTypeName";
        public const string Jsonb = "Jsonb";
        public const string Integer = "Integer";
        public const string Text = "Text";
        public const string Array = "Array";
        // NpgsqlDbType member names (NpgsqlDbType has no Int4Range/TsRange; an unknown name
        // silently left the parameter as text, which PostgreSQL rejects for a range column).
        public const string Int4Range = "IntegerRange";
        public const string Int8Range = "BigIntRange";
        public const string TsRange = "TimestampRange";
        public const string TsTzRange = "TimestampTzRange";
        public const string DateRange = "DateRange";
        public const string NumRange = "NumericRange";
        public const string Inet = "Inet";
        public const string Cidr = "Cidr";
        public const string MacAddr = "MacAddr";
        public const string MacAddr8 = "MacAddr8";
        public const string Interval = "Interval";
        public const string Uuid = "Uuid";
        public const string Hstore = "Hstore";
    }

    internal static DatabaseTraits CreatePostgreSqlTraits() =>
        new(SupportedDatabase.PostgreSql, PostgreSqlFamilyExceptionTranslator)
        {
            SpatialFormat = SpatialWireFormat.ExtendedWkb,
            IntervalFormat = IntervalWireFormat.Iso8601,
            BindsNpgsqlValueTypes = true,
            RegisterTypeMappings = registry =>
            {
                RegisterPostgreSqlFamilyTypeMappings(registry, SupportedDatabase.PostgreSql);
                RegisterHStoreMapping(registry, SupportedDatabase.PostgreSql);
            }
        };

    // Aurora PostgreSQL has no type mappings or value formats of its own: its dialect binds
    // through TypeMappingProvider, which is PostgreSql (VAR-001).
    internal static DatabaseTraits CreateAuroraPostgreSqlTraits() =>
        new(SupportedDatabase.AuroraPostgreSql, PostgreSqlFamilyExceptionTranslator);

    /// <summary>
    /// The parameter mappings PostgreSQL shares with CockroachDB and YugabyteDB (Npgsql types,
    /// PostGIS-style EWKB spatial, bytea/text LOBs). Not hstore, which CockroachDB lacks.
    /// </summary>
    private protected static void RegisterPostgreSqlFamilyTypeMappings(AdvancedTypeRegistry registry,
        SupportedDatabase database)
    {
        // JSONB
        registry.RegisterMapping<JsonDocument>(database, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                param.GetType().GetProperty(NpgsqlNames.DataTypeName)?.SetValue(param, "jsonb");
                AdvancedTypeRegistry.SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Jsonb);
            }
        });

        // PostGIS and CockroachDB's built-in spatial types: EWKB bytes, produced by
        // SpatialConverter. Geometry was registered for PostgreSQL only and Geography for none, so
        // elsewhere the value object reached Npgsql (TYPE-002).
        var spatial = new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) => param.DbType = DbType.Binary
        };
        registry.RegisterMapping<Geometry>(database, spatial);
        registry.RegisterMapping<Geography>(database, spatial);

        // int[] and text[] arrays
        registry.RegisterMapping<int[]>(database, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                AdvancedTypeRegistry.SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Array,
                    NpgsqlNames.Integer);
            }
        });
        registry.RegisterMapping<string[]>(database, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                AdvancedTypeRegistry.SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Array,
                    NpgsqlNames.Text);
            }
        });

        // Ranges: int4range, tsrange, int8range, and (TYPE-009) daterange, numrange, tstzrange.
        RegisterNpgsqlTyped<Range<int>>(registry, database, DbType.Object, NpgsqlNames.Int4Range);
        RegisterNpgsqlTyped<Range<DateTime>>(registry, database, DbType.Object, NpgsqlNames.TsRange);
        RegisterNpgsqlTyped<Range<long>>(registry, database, DbType.Object, NpgsqlNames.Int8Range);
        RegisterNpgsqlTyped<Range<DateOnly>>(registry, database, DbType.Object, NpgsqlNames.DateRange);
        RegisterNpgsqlTyped<Range<decimal>>(registry, database, DbType.Object, NpgsqlNames.NumRange);
        RegisterNpgsqlTyped<Range<DateTimeOffset>>(registry, database, DbType.Object, NpgsqlNames.TsTzRange);

        // inet, cidr
        RegisterNpgsqlTyped<Inet>(registry, database, DbType.String, NpgsqlNames.Inet);
        RegisterNpgsqlTyped<Cidr>(registry, database, DbType.String, NpgsqlNames.Cidr);

        // macaddr (6-byte EUI-48) / macaddr8 (8-byte EUI-64) - dispatch on the actual address
        // width, since MacAddress supports both. By the time this callback runs the registered
        // MacAddressConverter has already unwrapped the value to a PhysicalAddress.
        registry.RegisterMapping<MacAddress>(database, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                var isEui64 = value is PhysicalAddress address && address.GetAddressBytes().Length == 8;
                AdvancedTypeRegistry.SetEnumProperty(param, NpgsqlNames.DbTypeProperty,
                    isEui64 ? NpgsqlNames.MacAddr8 : NpgsqlNames.MacAddr);
            }
        });

        // interval
        RegisterNpgsqlTyped<PostgreSqlInterval>(registry, database, DbType.Object, NpgsqlNames.Interval);

        // bytea and text LOBs
        registry.RegisterMapping<Stream>(database, new ProviderTypeMapping
        {
            DbType = DbType.Binary,
            ConfigureParameter = (param, value) => { param.DbType = DbType.Binary; }
        });
        registry.RegisterMapping<TextReader>(database, new ProviderTypeMapping
        {
            DbType = DbType.String,
            ConfigureParameter = (param, value) =>
            {
                param.DbType = DbType.String;
                AdvancedTypeRegistry.SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Text);
            }
        });

        // uuid
        RegisterNpgsqlTyped<Guid>(registry, database, DbType.Guid, NpgsqlNames.Uuid);
    }

    private static void RegisterNpgsqlTyped<T>(AdvancedTypeRegistry registry, SupportedDatabase database,
        DbType dbType, string npgsqlDbType)
    {
        registry.RegisterMapping<T>(database, new ProviderTypeMapping
        {
            DbType = dbType,
            ConfigureParameter = (param, value) =>
            {
                AdvancedTypeRegistry.SetEnumProperty(param, NpgsqlNames.DbTypeProperty, npgsqlDbType);
            }
        });
    }

    /// <summary>
    /// hstore. Without a mapping the raw HStore struct reached Npgsql, which rejects it ("Writing
    /// values of 'HStore' is not supported ..."); Npgsql 9's hstore handler needs
    /// NpgsqlDbType.Hstore with a Dictionary&lt;string,string?&gt; value (confirmed live).
    /// </summary>
    private protected static void RegisterHStoreMapping(AdvancedTypeRegistry registry, SupportedDatabase database)
    {
        registry.RegisterMapping<HStore>(database, new ProviderTypeMapping
        {
            DbType = DbType.Object,
            ConfigureParameter = (param, value) =>
            {
                if (value is HStore hstore)
                {
                    var dict = new Dictionary<string, string?>(hstore.Count, StringComparer.Ordinal);
                    foreach (var pair in hstore)
                    {
                        dict[pair.Key] = pair.Value;
                    }

                    param.Value = dict;
                }

                AdvancedTypeRegistry.SetEnumProperty(param, NpgsqlNames.DbTypeProperty, NpgsqlNames.Hstore);
            }
        });
    }
}
