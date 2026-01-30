// =============================================================================
// FILE: SqlDialect.cs
// PURPOSE: Abstract base class for all database-specific SQL dialects.
//
// AI SUMMARY:
// - Base class that all specific dialects (SqlServerDialect, etc.) inherit from.
// - Implements ISqlDialect interface with common SQL generation logic.
// - Key responsibilities:
//   * Parameter creation and naming (MakeParameterName, CreateDbParameter)
//   * Identifier quoting (WrapObjectName) - overridden by specific dialects
//   * Feature detection (SupportsMerge, SupportsInsertOnConflict, etc.)
//   * Type conversions for provider-specific quirks
//   * Database version detection (DetectDatabaseInfoAsync)
// - Performance optimizations:
//   * Caches wrapped names and parameter names
//   * Pools DbParameter instances to reduce allocations
//   * Pre-compiled type conversion delegates
//   * Pooled parameter name generation
// - Abstract properties for dialect-specific behavior:
//   * DatabaseType, ParameterMarker, QuotePrefix/Suffix
// - Session settings application via ApplySessionSettingsAsync().
// - MERGE/UPSERT SQL generation helpers.
// =============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.types;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Abstract base class implementing common SQL dialect functionality.
/// </summary>
/// <remarks>
/// <para>
/// This class provides the foundation for all database-specific dialects,
/// implementing common SQL generation logic and exposing abstract properties
/// for database-specific customization.
/// </para>
/// <para>
/// <strong>Key Features:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>Parameter creation with dialect-specific markers (@, :, ?, $)</description></item>
/// <item><description>Identifier quoting for reserved words and special characters</description></item>
/// <item><description>Feature detection (MERGE, ON CONFLICT, stored procedures)</description></item>
/// <item><description>Type conversion for provider-specific quirks</description></item>
/// </list>
/// <para>
/// <strong>Performance:</strong> Uses caching extensively for wrapped names,
/// parameter names, and type conversions to minimize allocations.
/// </para>
/// </remarks>
/// <seealso cref="ISqlDialect"/>
/// <seealso cref="SqlDialectFactory"/>
internal abstract class SqlDialect : ISqlDialect
{
    protected readonly DbProviderFactory Factory;
    protected readonly ILogger Logger;
    protected DbConnectionStringBuilder ConnectionStringBuilder { get; init; }
    private IDatabaseProductInfo? _productInfo;

    // Performance optimization: Cache frequently used parameter names to avoid repeated string operations
    private readonly ConcurrentDictionary<string, string> _trimmedNameCache = new();

    private readonly ConcurrentDictionary<string, string> _wrappedNameCache = new(StringComparer.Ordinal);

    // Pre-compiled parameter marker trimming for faster string operations
    private static readonly char[] _parameterMarkers = { '@', ':', '?', '$' };

    // Performance: Static parameter name pool to avoid allocations
    private static readonly char[] ValidNameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

    private static readonly string[] ParameterNamePool = GenerateParameterNamePool();
    private static int _parameterNamePoolIndex;

    // Type conversion delegates for hot paths - compiled once, reused everywhere
    private static readonly ConcurrentDictionary<DbType, Action<DbParameter, object?>> _typeConversionCache = new();

    // Precompiled common type conversions to avoid repeated pattern matching
    private static readonly IReadOnlyDictionary<DbType, Action<DbParameter, object?>> _commonConversions =
        new Dictionary<DbType, Action<DbParameter, object?>>
        {
            [DbType.Guid] = static (p, v) =>
            {
                p.DbType = DbType.String;
                if (v is Guid guid)
                {
                    p.Value = guid.ToString();
                    p.Size = 36;
                }
            },
            [DbType.Boolean] = static (p, v) =>
            {
                p.DbType = DbType.Int16;
                if (v is bool b)
                {
                    p.Value = b ? (short)1 : (short)0;
                }
            },
            [DbType.DateTimeOffset] = static (p, v) =>
            {
                p.DbType = DbType.DateTime;
                if (v is DateTimeOffset dto)
                {
                    p.Value = dto.DateTime;
                }
            }
        };

    // Simple parameter pool - avoid repeated factory calls for hot paths
    private readonly ConcurrentQueue<DbParameter> _parameterPool = new();
    private const int MaxPoolSize = 100; // Prevent unbounded growth
    protected static AdvancedTypeRegistry AdvancedTypes { get; } = AdvancedTypeRegistry.Shared;

    protected SqlDialect(DbProviderFactory factory, ILogger logger)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ConnectionStringBuilder = Factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
    }

    /// <summary>
    /// Gets the detected database product information. Call DetectDatabaseInfo first.
    /// </summary>
    public IDatabaseProductInfo ProductInfo => _productInfo ??
                                               throw new InvalidOperationException(
                                                   "Database info not detected. Call DetectDatabaseInfo first.");

    /// <summary>
    /// Whether database info has been detected
    /// </summary>
    public bool IsInitialized => _productInfo != null;

    // Core properties with SQL-92 defaults; override for database-specific behavior
    public abstract SupportedDatabase DatabaseType { get; }
    public virtual string ParameterMarker => "?";

    public virtual string ParameterMarkerAt(int ordinal)
    {
        return ParameterMarker;
    }

    public virtual bool SupportsNamedParameters => true;

    public virtual string RenderJsonArgument(string parameterMarker, IColumnInfo column)
    {
        return parameterMarker;
    }

    public virtual void TryMarkJsonParameter(DbParameter parameter, IColumnInfo column)
    {
        if (parameter == null)
        {
            throw new ArgumentNullException(nameof(parameter));
        }
    }

    public virtual bool SupportsSetValuedParameters => false;
    public virtual int MaxParameterLimit => 255;
    public virtual int MaxOutputParameters => 0;
    public virtual int ParameterNameMaxLength => 18;
    public virtual ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    /// <summary>
    /// The highest SQL standard level this database/version supports
    /// </summary>
    public virtual SqlStandardLevel MaxSupportedStandard =>
        IsInitialized ? ProductInfo.StandardCompliance : SqlStandardLevel.Sql92;

    // SQL standard defaults - can be overridden for database-specific behavior
    public virtual string QuotePrefix => "\""; // SQL-92 standard
    public virtual string QuoteSuffix => "\""; // SQL-92 standard
    public virtual string CompositeIdentifierSeparator => "."; // SQL-92 standard
    public virtual bool PrepareStatements => false;

    // SQL standard parameter name pattern (SQL-92)
    public virtual Regex ParameterNamePattern => new("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    // Feature support based on SQL standards and database capabilities
    public virtual bool SupportsIntegrityConstraints => MaxSupportedStandard >= SqlStandardLevel.Sql89;
    public virtual bool SupportsJoins => MaxSupportedStandard >= SqlStandardLevel.Sql92;
    public virtual bool SupportsOuterJoins => MaxSupportedStandard >= SqlStandardLevel.Sql92;
    public virtual bool SupportsSubqueries => MaxSupportedStandard >= SqlStandardLevel.Sql92;
    public virtual bool SupportsUnion => MaxSupportedStandard >= SqlStandardLevel.Sql92;

    // SQL:1999 (SQL3) features
    public virtual bool SupportsUserDefinedTypes => MaxSupportedStandard >= SqlStandardLevel.Sql99;
    public virtual bool SupportsArrayTypes => MaxSupportedStandard >= SqlStandardLevel.Sql99;
    public virtual bool SupportsRegularExpressions => MaxSupportedStandard >= SqlStandardLevel.Sql99;

    // SQL:2003 features
    public virtual bool SupportsMerge => MaxSupportedStandard >= SqlStandardLevel.Sql2003;
    public virtual bool SupportsXmlTypes => MaxSupportedStandard >= SqlStandardLevel.Sql2003;
    public virtual bool SupportsWindowFunctions => MaxSupportedStandard >= SqlStandardLevel.Sql2003;
    public virtual bool SupportsCommonTableExpressions => MaxSupportedStandard >= SqlStandardLevel.Sql2003;

    // SQL:2008 features
    public virtual bool SupportsInsteadOfTriggers => MaxSupportedStandard >= SqlStandardLevel.Sql2008;
    public virtual bool SupportsTruncateTable => MaxSupportedStandard >= SqlStandardLevel.Sql2008;

    // SQL:2011 features
    public virtual bool SupportsTemporalData => MaxSupportedStandard >= SqlStandardLevel.Sql2011;
    public virtual bool SupportsEnhancedWindowFunctions => MaxSupportedStandard >= SqlStandardLevel.Sql2011;

    // SQL:2016 features
    public virtual bool SupportsJsonTypes => MaxSupportedStandard >= SqlStandardLevel.Sql2016;
    public virtual bool SupportsRowPatternMatching => MaxSupportedStandard >= SqlStandardLevel.Sql2016;

    // SQL:2019 features
    public virtual bool SupportsMultidimensionalArrays => MaxSupportedStandard >= SqlStandardLevel.Sql2019;

    // SQL:2023 features
    public virtual bool SupportsPropertyGraphQueries => MaxSupportedStandard >= SqlStandardLevel.Sql2023;

    // Modern SQL/JSON feature gates (safe defaults)
    public virtual bool SupportsSqlJsonConstructors => false;
    public virtual bool SupportsJsonTable => false;
    public virtual bool SupportsMergeReturning => false;

    // Database-specific extensions (override as needed)
    public virtual bool SupportsInsertOnConflict => false; // PostgreSQL, SQLite extension
    public virtual bool SupportsOnDuplicateKey => false; // MySQL, MariaDB extension
    public virtual bool SupportsSavepoints => false;

    /// <summary>
    /// Gets the SQL statement to create a savepoint with the given name.
    /// Override for databases with non-standard syntax (e.g., SQL Server uses SAVE TRANSACTION).
    /// </summary>
    public virtual string GetSavepointSql(string name)
    {
        return $"SAVEPOINT {name}";
    }

    /// <summary>
    /// Gets the SQL statement to rollback to a savepoint with the given name.
    /// Override for databases with non-standard syntax (e.g., SQL Server uses ROLLBACK TRANSACTION).
    /// </summary>
    public virtual string GetRollbackToSavepointSql(string name)
    {
        return $"ROLLBACK TO SAVEPOINT {name}";
    }

    public virtual bool RequiresStoredProcParameterNameMatch => false;
    public virtual bool SupportsNamespaces => false; // SQL-92 does not require schema support

    /// <summary>
    /// Indicates whether MERGE UPDATE SET clause requires table alias prefix on target columns.
    /// SQL Server, Oracle: true (allows `UPDATE SET t.col = value`)
    /// PostgreSQL: false (requires `UPDATE SET col = value`, will error with alias prefix)
    /// </summary>
    public virtual bool MergeUpdateRequiresTargetAlias => true; // SQL-92 MERGE allows it (SQL Server, Oracle)

    /// <summary>
    /// Indicates whether this dialect represents an unknown database using the SQL-92 fallback.
    /// </summary>
    public bool IsFallbackDialect => ProductInfo.DatabaseType == SupportedDatabase.Unknown;

    /// <summary>
    /// Returns a warning if the SQL-92 fallback dialect is in use.
    /// </summary>
    public string GetCompatibilityWarning()
    {
        return IsFallbackDialect
            ? "Using SQL-92 fallback dialect - some features may be unavailable"
            : string.Empty;
    }

    /// <summary>
    /// Initializes the dialect with a safe default product info when detection cannot run.
    /// Intended for contexts that defer connection opening (e.g., Standard mode construction).
    /// </summary>
    public void InitializeUnknownProductInfo()
    {
        _productInfo ??= new DatabaseProductInfo
        {
            ProductName = "Unknown",
            ProductVersion = string.Empty,
            DatabaseType = DatabaseType,
            StandardCompliance = SqlStandardLevel.Sql92
        };
    }

    /// <summary>
    /// Indicates whether SQL:2003 or later features may be used.
    /// </summary>
    public bool CanUseModernFeatures => MaxSupportedStandard >= SqlStandardLevel.Sql2003;

    /// <summary>
    /// Indicates whether the database meets SQL-92 compatibility.
    /// </summary>
    public bool HasBasicCompatibility => MaxSupportedStandard >= SqlStandardLevel.Sql92;

    public virtual string WrapObjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var span = name.AsSpan();
        var trimmed = span.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var canonical = trimmed.Length == span.Length ? name : trimmed.ToString();
        return _wrappedNameCache.GetOrAdd(canonical, static (key, state) => state.BuildWrappedObjectName(key), this);
    }

    private string BuildWrappedObjectName(string identifier)
    {
        var prefix = QuotePrefix;
        var suffix = QuoteSuffix;
        var separator = CompositeIdentifierSeparator;

        var capacityHint = identifier.Length + ((prefix.Length + suffix.Length) << 1);
        var stackSize = capacityHint > SbLite.DefaultStack ? capacityHint : SbLite.DefaultStack;
        var builder = SbLite.Create(stackalloc char[stackSize]);
        var separatorSpan = separator.AsSpan();
        var prefixSpan = prefix.AsSpan();
        var suffixSpan = suffix.AsSpan();
        var value = identifier.AsSpan();
        var hasSeparator = separatorSpan.Length > 0;

        var consumed = 0;
        var wroteSegment = false;

        while (consumed < value.Length)
        {
            var remaining = value.Slice(consumed);
            var separatorIndex = hasSeparator ? IndexOf(remaining, separatorSpan) : -1;

            ReadOnlySpan<char> segment;
            if (separatorIndex >= 0)
            {
                segment = remaining.Slice(0, separatorIndex);
                consumed += separatorIndex + separatorSpan.Length;
            }
            else
            {
                segment = remaining;
                consumed = value.Length;
            }

            segment = TrimWhitespace(segment);
            if (segment.Length == 0)
            {
                continue;
            }

            if (wroteSegment)
            {
                builder.Append(separator);
            }

            builder.Append(prefix);
            AppendWithoutQuotes(ref builder, segment, prefixSpan, suffixSpan);
            builder.Append(suffix);

            wroteSegment = true;
        }

        var result = wroteSegment ? builder.ToString() : string.Empty;
        return result;
    }

    protected static string BuildSessionSettingsScript(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> current,
        Func<string, string, string> formatter)
    {
        if (expected.Count == 0)
        {
            return string.Empty;
        }

        var builder = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        foreach (var kvp in expected)
        {
            current.TryGetValue(kvp.Key, out var currentValue);
            if (string.Equals(currentValue, kvp.Value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(formatter(kvp.Key, kvp.Value));
        }

        return builder.ToString();
    }

    protected SessionSettingsResult EvaluateSessionSettings(
        IDbConnection connection,
        Func<IDbConnection, SessionSettingsResult> evaluator,
        Func<SessionSettingsResult> fallback,
        string failureMessage)
    {
        try
        {
            return evaluator(connection);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, failureMessage);
            return fallback();
        }
    }

    protected async Task<T?> ExecuteScalarQueryAsync<T>(
        ITrackedConnection connection,
        string query,
        Func<object?, T?> converter,
        Func<Exception, T?>? onError = null)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = query;
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return converter(result);
        }
        catch (Exception ex) when (onError != null)
        {
            return onError(ex);
        }
    }

    protected readonly record struct SessionSettingsResult(
        string Settings,
        IReadOnlyDictionary<string, string> Snapshot,
        bool UsedFallback);

    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> span)
    {
        var start = 0;
        var end = span.Length - 1;

        while (start <= end && char.IsWhiteSpace(span[start]))
        {
            start++;
        }

        while (end >= start && char.IsWhiteSpace(span[end]))
        {
            end--;
        }

        return start > end ? ReadOnlySpan<char>.Empty : span.Slice(start, end - start + 1);
    }

    private static void AppendWithoutQuotes(ref StringBuilderLite builder, ReadOnlySpan<char> value,
        ReadOnlySpan<char> prefix, ReadOnlySpan<char> suffix)
    {
        var index = 0;
        while (index < value.Length)
        {
            if (!prefix.IsEmpty && StartsWith(value, prefix, index))
            {
                index += prefix.Length;
                continue;
            }

            if (!suffix.IsEmpty && StartsWith(value, suffix, index))
            {
                index += suffix.Length;
                continue;
            }

            builder.Append(value[index]);
            index++;
        }
    }

    private static bool StartsWith(ReadOnlySpan<char> span, ReadOnlySpan<char> value, int start)
    {
        if (start < 0 || start + value.Length > span.Length)
        {
            return false;
        }

        return span.Slice(start, value.Length).SequenceEqual(value);
    }

    private static int IndexOf(ReadOnlySpan<char> span, ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return -1;
        }

        if (value.Length == 1)
        {
            return span.IndexOf(value[0]);
        }

        for (var i = 0; i <= span.Length - value.Length; i++)
        {
            if (span.Slice(i, value.Length).SequenceEqual(value))
            {
                return i;
            }
        }

        return -1;
    }

    public virtual string MakeParameterName(string parameterName)
    {
        if (!SupportsNamedParameters)
        {
            return "?";
        }

        if (parameterName is null)
        {
            return ParameterMarker;
        }

        parameterName = parameterName.Replace("@", string.Empty)
            .Replace(":", string.Empty)
            .Replace("?", string.Empty);

        return string.Concat(ParameterMarker, parameterName);
    }

    public virtual string MakeParameterName(DbParameter dbParameter)
    {
        return MakeParameterName(dbParameter.ParameterName);
    }

    public virtual string UpsertIncomingColumn(string columnName)
    {
        throw new NotSupportedException(
            $"UpsertIncomingColumn is dialect-specific. Override required for {DatabaseType}.");
    }

    /// <summary>
    /// Optional alias used to reference the incoming row during upsert operations.
    /// </summary>
    public virtual string? UpsertIncomingAlias => null;

    /// <summary>
    /// Get a parameter from the pool or create a new one. For internal use by hot paths.
    /// </summary>
    private DbParameter GetPooledParameter(out bool pooled)
    {
        if (_parameterPool.TryDequeue(out var param))
        {
            pooled = true;
            // Reset pooled parameter to clean state
            param.ParameterName = string.Empty;
            param.Value = null;
            param.DbType = DbType.Object;
            param.Direction = ParameterDirection.Input;
            param.Size = 0;
            param.Precision = 0;
            param.Scale = 0;
            return param;
        }

        pooled = false;
        return Factory.CreateParameter() ?? throw new InvalidOperationException("Failed to create parameter.");
    }

    /// <summary>
    /// Return a parameter to the pool for reuse. Call this when parameter is no longer needed.
    /// </summary>
    internal void ReturnParameterToPool(DbParameter parameter)
    {
        if (_parameterPool.Count < MaxPoolSize)
        {
            _parameterPool.Enqueue(parameter);
        }
        // If pool is full, let it get garbage collected
    }

    public virtual DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        var traceTimings = Logger.IsEnabled(LogLevel.Debug) && IsParameterTimingEnabled();
        var start = traceTimings ? Stopwatch.GetTimestamp() : 0;
        var parameter = GetPooledParameter(out var pooled);

        if (string.IsNullOrWhiteSpace(name))
        {
            name = GenerateRandomName(5, ParameterNameMaxLength);
        }
        else if (SupportsNamedParameters)
        {
            // Strip parameter marker prefix if present, then validate the remaining name
            var nameToValidate = name;
            if (name.Length > 0 && (name[0] == '@' || name[0] == ':' || name[0] == '?' || name[0] == '$'))
            {
                nameToValidate = name.Substring(1);
                name = nameToValidate; // Use the stripped name
            }

            // Validate that parameter names only contain alphanumeric characters and underscores
            if (!IsValidParameterName(nameToValidate))
            {
                throw new ArgumentException(
                    $"Parameter name '{name}' contains invalid characters. Only alphanumeric characters and underscores are allowed.",
                    nameof(name));
            }
        }

        parameter.ParameterName = name;

        // Performance: Inline null check to avoid method call overhead
        var valueIsNull = value == null || value is DBNull;
        var runtimeType = valueIsNull ? null : ResolveClrType(value);
        var handled = runtimeType != null &&
                      AdvancedTypes.IsMappedType(runtimeType) &&
                      AdvancedTypes.TryConfigureParameter(parameter, runtimeType, value, DatabaseType);

        if (!handled)
        {
            parameter.DbType = type;
            parameter.Value = valueIsNull ? DBNull.Value : value!;
        }

        if (!SupportsNamedParameters)
        {
            parameter.ParameterName = string.Empty;

            if (!handled && !valueIsNull && _commonConversions.TryGetValue(parameter.DbType, out var converter))
            {
                converter(parameter, value);
            }
        }

        if (!handled && !valueIsNull)
        {
            if (value is string s && (parameter.DbType == DbType.String || parameter.DbType == DbType.AnsiString ||
                                      parameter.DbType == DbType.StringFixedLength ||
                                      parameter.DbType == DbType.AnsiStringFixedLength))
            {
                parameter.Size = Math.Max(s.Length, 1);
            }
            else if (parameter.DbType == DbType.Decimal && value is decimal dec)
            {
                var (prec, scale) = DecimalHelpers.Infer(dec);
                parameter.Precision = (byte)prec;
                parameter.Scale = (byte)scale;
            }
        }

        if (traceTimings)
        {
            var elapsedUs = TicksToMicroseconds(Stopwatch.GetTimestamp() - start);
            Logger.LogDebug(
                "DbParameter timing pooled={Pooled} dbType={DbType} hasValue={HasValue} handled={Handled} elapsed={ElapsedUs:0.000}us",
                pooled,
                type,
                !valueIsNull,
                handled,
                elapsedUs);
        }

        return parameter;
    }

    private static bool IsParameterTimingEnabled()
    {
        var value = Environment.GetEnvironmentVariable("PENGDOWS_PARAM_TIMING");
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static double TicksToMicroseconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0d;
        }

        return ticks * 1_000_000d / Stopwatch.Frequency;
    }

    private static Type? ResolveClrType<T>(T value)
    {
        var type = typeof(T);
        if (type != typeof(object))
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        return value?.GetType();
    }

    public virtual DbParameter CreateDbParameter(string? name, DbType type, object? value)
    {
        return CreateDbParameter<object?>(name, type, value);
    }

    public virtual DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return CreateDbParameter(null, type, value);
    }

    // Methods for database-specific operations
    public virtual string GetVersionQuery()
    {
        return string.Empty;
    }

    public virtual string GetDatabaseVersion(ITrackedConnection connection)
    {
        try
        {
            return GetDatabaseVersionAsync(connection).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve database version");
            return $"Error retrieving version: {ex.Message}";
        }
    }

    // Optional hook for dialect initialization after connection is established
    public virtual Task PostInitialize(ITrackedConnection connection)
    {
        return Task.CompletedTask;
    }

    public virtual DataTable GetDataSourceInformationSchema(ITrackedConnection connection)
    {
        try
        {
            var schema = connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
            if (schema.Rows.Count > 0)
            {
                return schema;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Data source information schema unavailable; using SQL-92 defaults");
        }

        return DataSourceInformation.BuildEmptySchema(
            "Unknown Database (SQL-92 Compatible)",
            "Unknown Version",
            Regex.Escape(ParameterMarker),
            ParameterMarker,
            ParameterNameMaxLength,
            ParameterNamePattern.ToString(),
            ParameterNamePattern.ToString(),
            SupportsNamedParameters);
    }

    [Obsolete("Use GetConnectionSessionSettings(IDatabaseContext,bool).")]
    public virtual string GetConnectionSessionSettings()
    {
        return string.Empty;
    }

    public virtual string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        return BuildSessionSettings(GetBaseSessionSettings(), GetReadOnlySessionSettings(), readOnly);
    }

    public virtual void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        var connectionString = context.ConnectionString;

        // Apply read-only connection string modification if supported
        if (readOnly)
        {
            var readOnlyParam = GetReadOnlyConnectionParameter();
            if (!string.IsNullOrEmpty(readOnlyParam))
            {
                connectionString = BuildReadOnlyConnectionString(connectionString, readOnlyParam);
            }
        }

        connection.ConnectionString = connectionString;

        // Hook for database-specific connection configuration
        ConfigureProviderSpecificSettings(connection, context, readOnly);
    }

    internal virtual string GetReadOnlyConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var readOnlyParam = GetReadOnlyConnectionParameter();
        if (string.IsNullOrEmpty(readOnlyParam))
        {
            return connectionString;
        }

        return BuildReadOnlyConnectionString(connectionString, readOnlyParam);
    }

    /// <summary>
    /// Default implementation checks for NotSupportedException and InvalidOperationException
    /// </summary>
    public virtual bool ShouldDisablePrepareOn(Exception ex)
    {
        return ex is NotSupportedException or InvalidOperationException;
    }

    [Obsolete("Use the overload accepting context and readOnly.")]
    public virtual void ApplyConnectionSettings(IDbConnection connection)
    {
    }

    public virtual void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
    }

    public virtual bool IsReadCommittedSnapshotOn(ITrackedConnection connection)
    {
        return false;
    }

    public virtual bool IsSnapshotIsolationOn(ITrackedConnection connection)
    {
        return false;
    }

    public virtual bool IsUniqueViolation(DbException ex)
    {
        return false;
    }

    /// <summary>
    /// Detects database product information from the connection
    /// </summary>
    public virtual async Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection)
    {
        if (_productInfo != null)
        {
            return _productInfo;
        }

        try
        {
            var versionString = await GetDatabaseVersionAsync(connection);
            var productName = await GetProductNameAsync(connection) ?? ExtractProductNameFromVersion(versionString);
            var parsedVersion = ParseVersion(versionString);
            var databaseType = InferDatabaseTypeFromInfo(productName, versionString);
            var standardCompliance = DetermineStandardCompliance(parsedVersion);

            _productInfo = new DatabaseProductInfo
            {
                ProductName = productName,
                ProductVersion = versionString,
                ParsedVersion = parsedVersion,
                DatabaseType = databaseType,
                StandardCompliance = standardCompliance
            };

            Logger.LogInformation("Detected database: {ProductName} {Version} (SQL Standard: {Standard})", productName,
                versionString, standardCompliance);
            return _productInfo;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to detect database information");

            _productInfo = new DatabaseProductInfo
            {
                ProductName = "Unknown",
                // Surface meaningful context for tests/diagnostics when version retrieval fails
                ProductVersion = $"Error retrieving version: {ex.Message}",
                DatabaseType = DatabaseType,
                StandardCompliance = SqlStandardLevel.Sql92
            };
            return _productInfo;
        }
    }

    #region Centralized Utility Methods for Derived Dialects

    /// <summary>
    /// Gets the base session settings for this dialect. Override to provide database-specific settings.
    /// </summary>
    /// <returns>Base session settings SQL string</returns>
    public virtual string GetBaseSessionSettings()
    {
        return string.Empty;
    }

    /// <summary>
    /// Gets the read-only specific session settings. Override to provide database-specific read-only settings.
    /// </summary>
    /// <returns>Read-only session settings SQL string</returns>
    public virtual string GetReadOnlySessionSettings()
    {
        return string.Empty;
    }

    /// <summary>
    /// Gets the connection string parameter for read-only mode. Override to provide database-specific parameter.
    /// </summary>
    /// <returns>Connection string parameter for read-only mode, or null if not supported</returns>
    public virtual string? GetReadOnlyConnectionParameter()
    {
        return null;
    }

    /// <summary>
    /// Builds session settings by combining base and read-only settings
    /// </summary>
    /// <param name="baseSettings">Base session settings</param>
    /// <param name="readOnlySettings">Read-only specific settings</param>
    /// <param name="readOnly">Whether read-only mode is enabled</param>
    /// <returns>Combined session settings</returns>
    protected virtual string BuildSessionSettings(string baseSettings, string? readOnlySettings, bool readOnly)
    {
        if (readOnly && !string.IsNullOrEmpty(readOnlySettings))
        {
            return string.IsNullOrEmpty(baseSettings)
                ? readOnlySettings
                : $"{baseSettings}\n{readOnlySettings}";
        }

        return baseSettings;
    }

    /// <summary>
    /// Builds a read-only connection string by appending the read-only parameter
    /// </summary>
    /// <param name="connectionString">Base connection string</param>
    /// <param name="readOnlyParameter">Read-only parameter to append</param>
    /// <returns>Modified connection string</returns>
    protected virtual string BuildReadOnlyConnectionString(string connectionString, string readOnlyParameter)
    {
        return $"{connectionString};{readOnlyParameter}";
    }

    /// <summary>
    /// Checks if the connection string represents a memory database
    /// </summary>
    /// <param name="connectionString">Connection string to check</param>
    /// <returns>True if this is a memory database</returns>
    protected virtual bool IsMemoryDatabase(string connectionString)
    {
        return connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the database version is at least the specified version
    /// </summary>
    /// <param name="major">Required major version</param>
    /// <param name="minor">Required minor version (default: 0)</param>
    /// <param name="build">Required build/patch version (default: 0)</param>
    /// <returns>True if version meets requirements</returns>
    protected virtual bool IsVersionAtLeast(int major, int minor = 0, int build = 0)
    {
        if (!IsInitialized || ProductInfo.ParsedVersion == null)
        {
            return false;
        }

        var v = ProductInfo.ParsedVersion;
        var vMinor = v.Minor < 0 ? 0 : v.Minor;
        var vBuild = v.Build < 0 ? 0 : v.Build;

        return v.Major > major ||
               (v.Major == major && vMinor > minor) ||
               (v.Major == major && vMinor == minor && vBuild >= build);
    }

    /// <summary>
    /// Gets a mapping of major versions to SQL standard levels. Override in derived classes.
    /// </summary>
    /// <returns>Dictionary mapping major version numbers to standard compliance levels</returns>
    public virtual Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>();
    }

    /// <summary>
    /// Gets the default SQL standard level when version information is unavailable
    /// </summary>
    /// <returns>Default SQL standard level</returns>
    public virtual SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql92;
    }

    /// <summary>
    /// Hook for database-specific connection configuration. Override to provide custom logic.
    /// </summary>
    /// <param name="connection">Database connection to configure</param>
    /// <param name="context">Database context</param>
    /// <param name="readOnly">Whether this is a read-only connection</param>
    public virtual void ConfigureProviderSpecificSettings(IDbConnection connection, IDatabaseContext context,
        bool readOnly)
    {
        // Default implementation does nothing - override in derived classes
    }

    // Async convenience for tests; default is no-op
    public virtual Task ConfigureProviderSpecificSettingsAsync(IDbConnection connection)
    {
        return Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// Determines SQL standard compliance based on database version.
    /// Default implementation uses version mapping from GetMajorVersionToStandardMapping().
    /// Override for complex version logic.
    /// </summary>
    public virtual SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return GetDefaultStandardLevel();
        }

        var mapping = GetMajorVersionToStandardMapping();
        if (mapping.Count == 0)
        {
            return GetDefaultStandardLevel();
        }

        // Find the highest version that the current version meets or exceeds
        var applicableVersions = mapping
            .Where(kvp => version.Major >= kvp.Key)
            .OrderByDescending(kvp => kvp.Key)
            .ToList();

        if (applicableVersions.Count == 0)
        {
            return GetDefaultStandardLevel();
        }

        return applicableVersions[0].Value;
    }

    public virtual IDatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection)
    {
        return DetectDatabaseInfoAsync(connection).GetAwaiter().GetResult();
    }

    public virtual async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        // Minimal, test-friendly behavior:
        // - Try dialect-provided query if any; otherwise, try SELECT version()
        // - If it throws, let the exception propagate so higher levels can decide how to handle
        // - If it returns null/empty, return empty
        var preferred = GetVersionQuery();
        var query = !string.IsNullOrWhiteSpace(preferred) ? preferred : "SELECT version()";

        var result = await ExecuteScalarQueryAsync(connection, query, static value => value?.ToString() ?? string.Empty)
            .ConfigureAwait(false);

        return result ?? string.Empty;
    }

    public virtual Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        try
        {
            var schema = connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
            if (schema.Rows.Count > 0)
            {
                var productName = schema.Rows[0].Field<string>("DataSourceProductName");
                if (!string.IsNullOrEmpty(productName))
                {
                    if (DatabaseType == SupportedDatabase.Unknown)
                    {
                        Logger.LogWarning(
                            "Using SQL-92 fallback dialect for detected database: {ProductName}",
                            productName);
                    }

                    return Task.FromResult<string?>(productName);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not get product name from schema metadata");
        }

        if (DatabaseType == SupportedDatabase.Unknown)
        {
            Logger.LogWarning(
                "Using SQL-92 fallback dialect for unknown database product");
        }

        return Task.FromResult<string?>(null);
    }

    public virtual string ExtractProductNameFromVersion(string versionString)
    {
        var lower = versionString?.ToLowerInvariant() ?? string.Empty;

        if (lower.Contains("microsoft sql server"))
        {
            return "Microsoft SQL Server";
        }

        if (lower.Contains("mysql"))
        {
            return "MySQL";
        }

        if (lower.Contains("mariadb"))
        {
            return "MariaDB";
        }

        if (lower.Contains("postgresql"))
        {
            return "PostgreSQL";
        }

        if (lower.Contains("cockroach"))
        {
            return "CockroachDB";
        }

        if (lower.Contains("oracle"))
        {
            return "Oracle Database";
        }

        if (lower.Contains("sqlite"))
        {
            return "SQLite";
        }

        if (lower.Contains("firebird"))
        {
            return "Firebird";
        }

        return "Unknown Database (SQL-92 Compatible)";
    }

    protected virtual SupportedDatabase InferDatabaseTypeFromInfo(string productName, string versionString)
    {
        var combined = $"{productName} {versionString}".ToLowerInvariant();

        if (combined.Contains("sql server"))
        {
            return SupportedDatabase.SqlServer;
        }

        if (combined.Contains("mariadb"))
        {
            return SupportedDatabase.MariaDb;
        }

        if (combined.Contains("mysql"))
        {
            return SupportedDatabase.MySql;
        }

        if (combined.Contains("cockroach"))
        {
            return SupportedDatabase.CockroachDb;
        }

        if (combined.Contains("npgsql") || combined.Contains("postgres"))
        {
            return SupportedDatabase.PostgreSql;
        }

        if (combined.Contains("oracle"))
        {
            return SupportedDatabase.Oracle;
        }

        if (combined.Contains("sqlite"))
        {
            return SupportedDatabase.Sqlite;
        }

        if (combined.Contains("firebird"))
        {
            return SupportedDatabase.Firebird;
        }

        if (combined.Contains("duckdb") || combined.Contains("duck db"))
        {
            return SupportedDatabase.DuckDB;
        }

        return DatabaseType;
    }

    public virtual Version? ParseVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        var matches = Regex.Matches(versionString, @"\d+(?:\.\d+)+");
        if (matches.Count > 0)
        {
            if (Version.TryParse(matches[^1].Value, out var detailed))
            {
                return detailed;
            }
        }

        var fallback = Regex.Match(versionString, @"\d+");
        if (fallback.Success && Version.TryParse(fallback.Value, out var simple))
        {
            return simple;
        }

        if (!string.IsNullOrWhiteSpace(versionString))
        {
            Logger.LogWarning(
                "Unable to parse database version '{Version}' for {DatabaseType}; falling back to default SQL compliance.",
                versionString, DatabaseType);
        }

        return null;
    }

    /// <summary>
    /// Pre-generates a pool of parameter names for high-performance parameter creation.
    /// </summary>
    private static string[] GenerateParameterNamePool()
    {
        const int poolSize = 1000; // Enough for most queries
        const int nameLength = 5;
        var pool = new string[poolSize];

        const int firstCharMax = 52; // a-zA-Z
        var anyOtherMax = ValidNameChars.Length;

        Span<char> buffer = stackalloc char[nameLength];
        for (var p = 0; p < poolSize; p++)
        {
            buffer[0] = ValidNameChars[Random.Shared.Next(firstCharMax)];
            for (var i = 1; i < nameLength; i++)
            {
                buffer[i] = ValidNameChars[Random.Shared.Next(anyOtherMax)];
            }

            pool[p] = new string(buffer);
        }

        return pool;
    }

    /// <summary>
    /// Fast validation that parameter names only contain alphanumeric characters and underscores.
    /// No parameter markers (@, :, ?, $) are allowed.
    /// </summary>
    private static bool IsValidParameterName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // Fast path: check each character is alphanumeric or underscore
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!((c >= 'a' && c <= 'z') ||
                  (c >= 'A' && c <= 'Z') ||
                  (c >= '0' && c <= '9') ||
                  c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    public virtual int? GetMajorVersion(string versionString)
    {
        return ParseVersion(versionString)?.Major;
    }

    public string GenerateRandomName(int length, int parameterNameMaxLength)
    {
        // Fast path: Use pre-generated name from pool if length matches
        if (length == 5 && parameterNameMaxLength >= 5)
        {
            var index = Interlocked.Increment(ref _parameterNamePoolIndex);
            return ParameterNamePool[index % ParameterNamePool.Length];
        }

        // Slow path: Generate on demand for non-standard lengths
        var len = Math.Min(Math.Max(length, 2), parameterNameMaxLength);
        Span<char> buffer = stackalloc char[len];
        const int firstCharMax = 52; // a-zA-Z
        var anyOtherMax = ValidNameChars.Length;

        buffer[0] = ValidNameChars[Random.Shared.Next(firstCharMax)];
        for (var i = 1; i < len; i++)
        {
            buffer[i] = ValidNameChars[Random.Shared.Next(anyOtherMax)];
        }

        return new string(buffer);
    }

    /// <summary>
    /// Gets the database-specific query for retrieving the last inserted identity value.
    /// This is a fallback method - prefer using RETURNING/OUTPUT clauses when supported.
    /// </summary>
    /// <returns>SQL query to get the last inserted identity value</returns>
    public virtual string GetLastInsertedIdQuery()
    {
        throw new NotSupportedException($"GetLastInsertedIdQuery not implemented for {DatabaseType}. " +
                                        $"Prefer using RETURNING/OUTPUT clauses, or implement parameter-based row lookup.");
    }

    /// <summary>
    /// Indicates whether INSERT statements support RETURNING or OUTPUT clauses for retrieving generated values.
    /// This is the preferred method for getting inserted IDs as it's atomic and race-condition free.
    /// </summary>
    public virtual bool SupportsInsertReturning => false;

    /// <summary>
    /// Gets the SQL syntax for RETURNING/OUTPUT clause to retrieve the inserted ID.
    /// Only valid when SupportsInsertReturning is true.
    /// </summary>
    /// <param name="idColumnName">The name of the identity/auto-increment column</param>
    /// <returns>The RETURNING/OUTPUT clause SQL</returns>
    public virtual string GetInsertReturningClause(string idColumnName)
    {
        if (!SupportsInsertReturning)
        {
            throw new NotSupportedException($"{DatabaseType} does not support INSERT RETURNING/OUTPUT clauses.");
        }

        return $"RETURNING {WrapObjectName(idColumnName)}";
    }

    /// <summary>
    /// Gets the preferred strategy for retrieving generated primary key values after INSERT.
    /// This determines the hierarchy: inline RETURNING > session functions > correlation tokens > natural key lookup.
    /// </summary>
    public virtual GeneratedKeyPlan GetGeneratedKeyPlan()
    {
        // Oracle special case: sequence prefetch is preferred even though it supports RETURNING
        if (DatabaseType == SupportedDatabase.Oracle)
        {
            return GeneratedKeyPlan.PrefetchSequence;
        }

        // First preference: inline RETURNING/OUTPUT clauses (atomic, single round-trip)
        if (SupportsInsertReturning)
        {
            return DatabaseType switch
            {
                SupportedDatabase.SqlServer => GeneratedKeyPlan.OutputInserted,
                _ => GeneratedKeyPlan.Returning
            };
        }

        // Second preference: session-scoped functions (safe on same connection)
        if (HasSessionScopedLastIdFunction())
        {
            return GeneratedKeyPlan.SessionScopedFunction;
        }

        // Universal fallback: correlation token (works everywhere, requires two round-trips)
        return GeneratedKeyPlan.CorrelationToken;
    }

    /// <summary>
    /// Determines if this database has a safe session-scoped last insert ID function.
    /// </summary>
    public virtual bool HasSessionScopedLastIdFunction()
    {
        return DatabaseType switch
        {
            SupportedDatabase.MySql => true, // LAST_INSERT_ID() is per-connection safe
            SupportedDatabase.MariaDb => true, // LAST_INSERT_ID() is per-connection safe
            SupportedDatabase.Sqlite => true, // last_insert_rowid() is per-connection safe
            SupportedDatabase.SqlServer => true, // SCOPE_IDENTITY() is per-batch/scope safe
            SupportedDatabase.PostgreSql => false, // lastval() can point at wrong sequence
            SupportedDatabase.DuckDB => false, // prefer RETURNING over lastval()
            _ => false
        };
    }

    /// <summary>
    /// Generates a correlation token query to retrieve the ID of an inserted row.
    /// This is the safest universal fallback that works on any database.
    /// </summary>
    /// <param name="tableName">The name of the table</param>
    /// <param name="idColumnName">The name of the identity/ID column</param>
    /// <param name="correlationTokenColumn">The name of the correlation token column</param>
    /// <param name="tokenParameterName">The parameter name for the token value</param>
    /// <returns>SQL query to find the inserted row by correlation token</returns>
    public virtual string GetCorrelationTokenLookupQuery(string tableName, string idColumnName,
        string correlationTokenColumn, string tokenParameterName)
    {
        return $"SELECT {WrapObjectName(idColumnName)} FROM {WrapObjectName(tableName)} " +
               $"WHERE {WrapObjectName(correlationTokenColumn)} = {tokenParameterName}";
    }

    /// <summary>
    /// Generates a natural key lookup query (last resort, requires unique constraints).
    /// Only safe when the lookup columns have a unique constraint and no data transformation occurs.
    /// </summary>
    /// <param name="tableName">The name of the table</param>
    /// <param name="idColumnName">The name of the identity/ID column</param>
    /// <param name="columnNames">List of non-identity column names (must have unique constraint)</param>
    /// <param name="parameterNames">List of parameter names corresponding to the columns</param>
    /// <returns>SQL query to find the inserted row by natural key</returns>
    /// <exception cref="InvalidOperationException">Thrown when natural key lookup is unsafe</exception>
    public virtual string GetNaturalKeyLookupQuery(string tableName, string idColumnName,
        IReadOnlyList<string> columnNames, IReadOnlyList<string> parameterNames)
    {
        if (columnNames.Count != parameterNames.Count)
        {
            throw new ArgumentException("Column names and parameter names must have the same count");
        }

        if (columnNames.Count == 0)
        {
            throw new InvalidOperationException(
                "Natural key lookup requires at least one column. Consider using correlation token fallback instead.");
        }

        // This is a dangerous operation - require explicit acknowledgment
        Logger.LogWarning(
            "Using natural key lookup for table {TableName} with columns [{Columns}]. " +
            "This is only safe if these columns have a unique constraint and no data transformation occurs during INSERT. " +
            "Consider using correlation token fallback for better safety.",
            tableName, string.Join(", ", columnNames));

        var whereConditions = columnNames
            .Zip(parameterNames, (col, param) => $"{WrapObjectName(col)} = {param}")
            .ToList();

        var selectClause = DatabaseType switch
        {
            SupportedDatabase.SqlServer => $"SELECT TOP 1 {WrapObjectName(idColumnName)}",
            _ => $"SELECT {WrapObjectName(idColumnName)}"
        };

        var query = $"{selectClause} FROM {WrapObjectName(tableName)} WHERE " +
                    string.Join(" AND ", whereConditions);

        // For databases that support ORDER BY with identity columns, get the most recent
        if (SupportsIdentityColumns && DatabaseType != SupportedDatabase.Oracle)
        {
            query += $" ORDER BY {WrapObjectName(idColumnName)} DESC";
        }

        // Add LIMIT clause for non-SQL Server databases
        if (DatabaseType == SupportedDatabase.Oracle)
        {
            query += " AND ROWNUM = 1";
        }
        else if (DatabaseType != SupportedDatabase.SqlServer)
        {
            query += " LIMIT 1";
        }

        return query;
    }


    /// <summary>
    /// Generates the RETURNING or OUTPUT clause for INSERT statements to capture identity values.
    /// </summary>
    /// <param name="idColumnWrapped">Quoted identity column name</param>
    /// <returns>SQL clause like " RETURNING id" or " OUTPUT INSERTED.id"</returns>
    public virtual string RenderInsertReturningClause(string idColumnWrapped)
    {
        return DatabaseType switch
        {
            SupportedDatabase.PostgreSql => $" RETURNING {idColumnWrapped}",
            SupportedDatabase.SqlServer => $" OUTPUT INSERTED.{idColumnWrapped}",
            SupportedDatabase.Sqlite => $" RETURNING {idColumnWrapped}",
            SupportedDatabase.Oracle => $" RETURNING {idColumnWrapped} INTO ?",
            SupportedDatabase.Firebird => $" RETURNING {idColumnWrapped}",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Indicates whether the RETURNING/OUTPUT clause must appear before the VALUES keyword.
    /// </summary>
    public virtual bool InsertReturningClauseBeforeValues => false;

    // Connection pooling properties - safe defaults for SQL-92 compatibility
    /// <summary>
    /// True when the database provider supports external connection pooling.
    /// Default: true for most server databases, override to false for in-process databases.
    /// </summary>
    public virtual bool SupportsExternalPooling => true;

    /// <summary>
    /// The connection string parameter name for enabling/disabling pooling.
    /// Default: "Pooling" for most providers.
    /// </summary>
    public virtual string? PoolingSettingName => "Pooling";

    /// <summary>
    /// The connection string parameter name for minimum pool size.
    /// Default: null (no standard), must be overridden in provider-specific dialects.
    /// </summary>
    public virtual string? MinPoolSizeSettingName => null;

    /// <summary>
    /// The connection string parameter name for maximum pool size.
    /// Default: null (no standard), may be overridden in provider-specific dialects.
    /// </summary>
    public virtual string? MaxPoolSizeSettingName => null;

    /// <summary>
    /// The connection string parameter name for application/client identification.
    /// Used for telemetry and connection tagging in database monitoring tools.
    /// Default: null (not supported), override in provider-specific dialects.
    /// </summary>
    public virtual string? ApplicationNameSettingName => null;

    // Dialect defaults used when pool settings are not discoverable from the connection string.
    // These are intentionally internal (not part of the public API surface).
    internal virtual int DefaultMinPoolSize => ConnectionPoolingConfiguration.DefaultMinPoolSize;
    internal virtual int DefaultMaxPoolSize => 100;

    // ---- Legacy utility helpers (kept for test compatibility) ----
    public virtual bool SupportsIdentityColumns => false;
    public virtual bool SupportsReturningClause => SupportsInsertReturning;
    public SqlStandardLevel SqlStandardLevel => MaxSupportedStandard;

    public virtual bool IsUniqueViolation(Exception ex)
    {
        if (ex is DbException dbEx)
        {
            return IsUniqueViolation(dbEx);
        }

        return false;
    }

    // These helpers are intentionally private to match historical usage in tests via reflection.
    private static bool TryParseMajorVersion(string? version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var match = Regex.Match(version, "(\\d+)");
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups[1].Value, out major);
    }

    private static bool IsPrime(int n)
    {
        if (n < 2)
        {
            return false;
        }

        if (n % 2 == 0)
        {
            return n == 2;
        }

        var limit = (int)Math.Sqrt(n);
        for (var i = 3; i <= limit; i += 2)
        {
            if (n % i == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetPrime(int min)
    {
        if (min <= 2)
        {
            return 2;
        }

        var candidate = min % 2 == 0 ? min + 1 : min;
        while (!IsPrime(candidate))
        {
            candidate += 2;
        }

        return candidate;
    }
}