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
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
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

    /// <inheritdoc />
    /// <remarks>
    /// Firebird EXECUTE BLOCK requires all external parameters to be declared in the block header
    /// with explicit types. ADO.NET named parameters (@b0, @b1, ...) used in the generated
    /// EXECUTE BLOCK body are not automatically promoted to block-level input parameters,
    /// causing "Dynamic SQL Error -901" at prepare time. Individual INSERTs are used instead.
    /// The <see cref="BuildBatchInsertSql"/> override remains available for callers that generate
    /// the SQL for inspection or non-parameterised use.
    /// </remarks>
    public override bool SupportsBatchInsert => false;

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

        // Firebird uses EXECUTE BLOCK AS BEGIN INSERT INTO ...; INSERT INTO ...; END
        query.Append("EXECUTE BLOCK AS BEGIN ");

        var colList = string.Join(", ", columnNames);

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            query.Append("INSERT INTO ");
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

            query.Append("); ");
        }

        query.Append("END");
    }

    public override GeneratedKeyPlan GetGeneratedKeyPlan() => GeneratedKeyPlan.Returning;

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

    private string? _sessionSettings;

    public override async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        var productInfo = await base.DetectDatabaseInfoAsync(connection);

        if (_sessionSettings == null)
        {
            var result = GetFirebirdSessionSettings(connection);
            _sessionSettings = result.Settings;

            if (!string.IsNullOrWhiteSpace(_sessionSettings))
            {
                Logger.LogInformation("Applying Firebird session settings on first connect:\n{Settings}",
                    _sessionSettings);
            }
        }

        return productInfo;
    }

    private SessionSettingsResult GetFirebirdSessionSettings(IDbConnection connection)
    {
        return EvaluateSessionSettings(
            connection,
            conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT mon$sql_dialect FROM mon$database";

                var dialectValue = cmd.ExecuteScalar();
                var currentDialect = dialectValue != null ? Convert.ToInt32(dialectValue) : 3;

                var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mon$sql_dialect"] = currentDialect.ToString()
                };

                var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
                try
                {
                    // SET NAMES UTF8 is mandatory as we can't easily interrogate the session charset
                    sb.Append("SET NAMES UTF8;");

                    if (currentDialect != 3)
                    {
                        sb.AppendLine();
                        sb.Append("SET SQL DIALECT 3;");
                    }

                    return new SessionSettingsResult(sb.ToString(), snapshot, false);
                }
                finally
                {
                    sb.Dispose();
                }
            },
            () => new SessionSettingsResult(
                "SET NAMES UTF8;\nSET SQL DIALECT 3;",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                true),
            "Failed to check Firebird session settings");
    }

    public override string GetBaseSessionSettings()
    {
        return _sessionSettings ?? "SET NAMES UTF8;\nSET SQL DIALECT 3;";
    }

    public override string GetReadOnlySessionSettings()
    {
        return "SET TRANSACTION READ ONLY;";
    }

    internal override string? GetReadOnlyTransactionResetSql()
    {
        return "SET TRANSACTION READ WRITE;";
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
        // Intercept unsupported types before they reach the provider parameter's set_DbType
        var targetType = type;
        object? targetValue = value;

        if (type == DbType.Boolean)
        {
            targetType = DbType.Int16;
            if (value is bool boolValue)
            {
                targetValue = boolValue ? (short)1 : (short)0;
            }
        }
        else if (type == DbType.Guid)
        {
            targetType = DbType.String;
            if (value is Guid guidValue)
            {
                targetValue = guidValue.ToString("D");
            }
        }
        else if (type == DbType.DateTimeOffset)
        {
            // Firebird does not support DateTimeOffset; coerce to UTC DateTime
            targetType = DbType.DateTime;
            if (value is DateTimeOffset dto)
            {
                // Use Unspecified kind to prevent provider-side timezone adjustments
                targetValue = DateTime.SpecifyKind(dto.UtcDateTime, DateTimeKind.Unspecified);
            }
        }

        return base.CreateDbParameter<object?>(name, targetType, targetValue);
    }

    // Connection pooling properties for Firebird
    // SupportsExternalPooling, PoolingSettingName, DefaultMaxPoolSize inherited from base (true, "Pooling", 100)
    public override string? MinPoolSizeSettingName => "MinPoolSize";
    public override string? MaxPoolSizeSettingName => "MaxPoolSize";
}