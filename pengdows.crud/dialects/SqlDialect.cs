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
using System.Collections.Frozen;
using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.types;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Determines how <see cref="Guid"/> values are serialized to database parameters.
/// Each dialect declares its preferred storage format via <see cref="SqlDialect.GuidFormat"/>.
/// </summary>
internal enum GuidStorageFormat
{
    /// <summary>
    /// Leave <see cref="DbType.Guid"/> as-is and let the ADO.NET provider handle the
    /// conversion.  Correct for SQL Server (<c>uniqueidentifier</c>), MySQL/MariaDB
    /// (MySqlConnector maps <c>DbType.Guid</c> to <c>CHAR(36)</c> internally), and any
    /// provider whose driver natively understands <c>DbType.Guid</c>.
    /// </summary>
    PassThrough,

    /// <summary>
    /// Convert to <see cref="DbType.String"/> with a 36-character hyphenated UUID string
    /// (<c>"D"</c> format, e.g. <c>550e8400-e29b-41d4-a716-446655440000</c>).
    /// Used by SQLite, DuckDB, Oracle, and Snowflake.
    /// </summary>
    String,

    /// <summary>
    /// Convert to <see cref="DbType.Binary"/> with the 16-byte <c>ToByteArray()</c>
    /// representation.  Used by Firebird when the schema stores GUIDs as
    /// <c>CHAR(16) OCTETS</c>.
    /// </summary>
    Binary,
}

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

    private readonly ConcurrentDictionary<string, string> _wrappedNameCache = new(StringComparer.Ordinal);

    // Performance: Static parameter name pool to avoid allocations
    private static readonly char[] ValidNameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

    // Performance: Static counter for deterministic parameter naming.
    // uint avoids negative values on overflow; no modulo so names stay unique across 2^32 calls.
    private static uint _parameterNamePoolIndex;

    // Precompiled common type conversions to avoid repeated pattern matching
    private static readonly IReadOnlyDictionary<DbType, Action<DbParameter, object?>> _commonConversions =
        new Dictionary<DbType, Action<DbParameter, object?>>
        {
            [DbType.Guid] = static (p, v) =>
            {
                p.DbType = DbType.String;
                if (v is Guid guid)
                {
                    // Use string.Create with Guid.TryFormat to avoid allocations
                    p.Value = string.Create(36, guid, static (span, g) =>
                    {
                        g.TryFormat(span, out _, "D"); // "D" format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
                    });
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
                    p.Value = dto.UtcDateTime;
                }
            }
        };

    // Simple parameter pool - avoid repeated factory calls for hot paths
    private readonly ConcurrentQueue<DbParameter> _parameterPool = new();
    private const int MaxPoolSize = 100; // Prevent unbounded growth

    /// <summary>
    /// Provider-specific parameter property names that need special handling
    /// during pooling (reset) and cloning (copy). Shared with SqlContainer.
    /// </summary>
    internal static readonly string[] ProviderSpecificPropertyNames =
    {
        "NpgsqlDbType",
        "DataTypeName",
        "SqlDbType",
        "UdtTypeName",
        "OracleDbType",
        "MySqlDbType"
    };

    private static readonly ConcurrentDictionary<Type, Action<DbParameter>> ProviderSpecificResetters = new();

    private static readonly Action<DbParameter> NoopReset = static _ => { };

    /// <summary>
    /// CLR types that are never registered in AdvancedTypeRegistry across any supported dialect.
    /// Checking this set eliminates a ConcurrentDictionary lookup per parameter for the common case.
    /// NOTE: bool, Guid, DateTime, DateTimeOffset are intentionally excluded — dialects register
    /// them in AdvancedTypeRegistry (e.g. Oracle maps bool→Int16, Guid→VARCHAR2).
    /// </summary>
    private static readonly FrozenSet<Type> s_primitiveClrTypes = new HashSet<Type>
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
        typeof(char), typeof(string)
    }.ToFrozenSet();

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

    // Named-parameter providers allow the same @name to appear multiple times in SQL
    // with a single parameter object. Override to false for positional providers.
    public virtual bool SupportsRepeatedNamedParameters => SupportsNamedParameters;

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
    public virtual int MaxParameterLimit => 2000;

    /// <inheritdoc />
    public virtual int MaxRowsPerBatch => 1000;

    /// <inheritdoc />
    public virtual bool SupportsBatchInsert => true;

    /// <inheritdoc />
    public virtual bool SupportsBatchUpdate => false;

    /// <inheritdoc />
    public virtual void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue)
    {
        throw new NotSupportedException($"{DatabaseType} does not support optimized batch updates.");
    }

    /// <inheritdoc />
    public virtual void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
        ISqlQueryBuilder query)
    {
        BuildBatchInsertSql(tableName, columnNames, rowCount, query, null);
    }

    /// <summary>
    /// Specialized multi-row insert with optional value inspection for NULL inlining.
    /// </summary>
    public virtual void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount,
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

        query.Append("INSERT INTO ");
        query.Append(tableName);
        query.Append(" (");

        for (var i = 0; i < columnNames.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(columnNames[i]);
        }

        query.Append(") VALUES ");

        var paramIdx = 0;
        for (var row = 0; row < rowCount; row++)
        {
            if (row > 0)
            {
                query.Append(", ");
            }

            query.Append('(');
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
                    query.Append(paramIdx++.ToString(CultureInfo.InvariantCulture));
                }
            }

            query.Append(')');
        }
    }

    public virtual int MaxOutputParameters => 0;
    public virtual int ParameterNameMaxLength => 128;
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

    // Overridden by MySqlDialect to veto prepare after error 1461 fires, even when ForceManualPrepare is set.
    public virtual bool IsPrepareExhausted => false;

    /// <summary>
    /// Declares how this dialect serializes <see cref="Guid"/> values to database parameters.
    /// The base implementation returns <see cref="GuidStorageFormat.PassThrough"/>, which leaves
    /// <see cref="DbType.Guid"/> untouched and delegates to the ADO.NET provider (correct for
    /// SQL Server, MySQL, and MariaDB).  Dialects that need a specific wire format override this.
    /// </summary>
    protected virtual GuidStorageFormat GuidFormat => GuidStorageFormat.PassThrough;

    /// <summary>
    /// Serializes a <see cref="Guid"/> to the 16-byte representation used when
    /// <see cref="GuidFormat"/> is <see cref="GuidStorageFormat.Binary"/>.
    /// <para>
    /// The base implementation returns <see cref="Guid.ToByteArray()"/>, which uses .NET's
    /// mixed-endian layout (Data1/Data2/Data3 are little-endian, Data4 is big-endian).
    /// Dialects whose driver reads binary Guid columns using RFC 4122 big-endian byte order
    /// (e.g. Firebird's <c>CHAR(16) CHARACTER SET OCTETS</c>) must override this and return
    /// bytes in the big-endian layout so the round-trip is correct.
    /// </para>
    /// <para>
    /// <strong>Read-side note:</strong> If a driver returns binary Guid columns as
    /// <see cref="byte[]"/> (rather than a native <see cref="Guid"/>), the deserializer
    /// reconstructs the Guid via <c>new Guid(bytes)</c> which also expects mixed-endian.
    /// A dialect that overrides this method to write big-endian bytes must therefore also
    /// ensure its driver returns a native <see cref="Guid"/> on reads (as Firebird does for
    /// <c>CHAR(16) CHARACTER SET OCTETS</c>), or add a matching read-side coercion.
    /// </para>
    /// </summary>
    protected virtual byte[] SerializeGuidAsBinary(Guid guid) => guid.ToByteArray();

    // SQL standard parameter name pattern (SQL-92)
    public virtual Regex ParameterNamePattern => new("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Controls whether the common type coercions (Guid → string, bool → Int16,
    /// DateTimeOffset → UtcDateTime) are applied during parameter creation.
    /// <para>
    /// Defaults to <c>!SupportsNamedParameters</c> so positional providers (ODBC-style)
    /// always get conversions. Named-parameter dialects that require the same coercions
    /// (e.g., a vendor driver that rejects <see cref="System.Data.DbType.Guid"/> natively)
    /// can override this to <c>true</c>.
    /// </para>
    /// </summary>
    protected virtual bool NeedsCommonConversions => !SupportsNamedParameters;

    // Feature support based on SQL standards and database capabilities
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
    public virtual bool SupportsDropTableIfExists => true;

    /// <summary>
    /// Gets the SQL statement to create a savepoint with the given name.
    /// Override for databases with non-standard syntax (e.g., SQL Server uses SAVE TRANSACTION).
    /// </summary>
    public virtual string GetSavepointSql(string name)
    {
        return $"SAVEPOINT {WrapObjectName(name)}";
    }

    /// <summary>
    /// Gets the SQL statement to rollback to a savepoint with the given name.
    /// Override for databases with non-standard syntax (e.g., SQL Server uses ROLLBACK TRANSACTION).
    /// </summary>
    public virtual string GetRollbackToSavepointSql(string name)
    {
        return $"ROLLBACK TO SAVEPOINT {WrapObjectName(name)}";
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
    public bool IsFallbackDialect => DatabaseType == SupportedDatabase.Unknown;

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

    public virtual string WrapSimpleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var prefix = QuotePrefix;
        var suffix = QuoteSuffix;
        
        // Fast path for clean identifiers: if it doesn't contain the quote char, simple concat.
        // We only care about the suffix char (the closer), as that's the one that breaks the string.
        var quoteChar = suffix.Length == 1 ? suffix[0] : (char)0;
        if (quoteChar == 0 || name.IndexOf(quoteChar) < 0)
        {
            return prefix + name + suffix;
        }

        var builder = SbLite.Create(stackalloc char[name.Length + prefix.Length + suffix.Length + 4]);
        builder.Append(prefix);
        AppendWithEscaping(ref builder, name.AsSpan(), prefix.AsSpan(), suffix.AsSpan());
        builder.Append(suffix);
        return builder.ToString();
    }

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

            // IDEMPOTENCY: If segment is already wrapped in prefix/suffix, leave it alone.
            if (segment.Length >= (prefixSpan.Length + suffixSpan.Length) &&
                segment.StartsWith(prefixSpan) && 
                segment.EndsWith(suffixSpan))
            {
                builder.Append(segment);
            }
            else
            {
                builder.Append(prefix);
                AppendWithEscaping(ref builder, segment, prefixSpan, suffixSpan);
                builder.Append(suffix);
            }

            wroteSegment = true;
        }

        var result = wroteSegment ? builder.ToString() : string.Empty;
        return result;
    }

    /// <summary>
    /// Gets the final, optimized session initialization string for the given read-only intent.
    /// Dialects should override this to combine baseline and intent settings into a single
    /// SQL statement (where supported) to ensure 1 RTT and 1 execution on the server.
    /// </summary>
    public virtual string GetFinalSessionSettings(bool readOnly)
    {
        var baseline = GetBaseSessionSettings();
        var intent = readOnly ? GetReadOnlySessionSettings() : GetReadOnlyTransactionResetSql();

        if (string.IsNullOrWhiteSpace(baseline)) return intent ?? string.Empty;
        if (string.IsNullOrWhiteSpace(intent)) return baseline;

        // Default: Multiple statements in a single command batch (1 RTT)
        return baseline.TrimEnd(';') + ";\n" + intent;
    }

    /// <summary>
    /// Builds a session settings script from the expected and current values.
    /// Since pengdows.crud 2.0 enforces an "Always SET" policy for session integrity in pooled
    /// environments, this method always returns the full set of expected settings to ensure
    /// that any session pollution from prior pool users is overwritten.
    /// </summary>
    protected static string BuildSessionSettingsScript(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> current,
        Func<string, string, string> formatter,
        string separator = "\n")
    {
        if (expected.Count == 0)
        {
            return string.Empty;
        }

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            var first = true;
            foreach (var kvp in expected)
            {
                if (!first)
                {
                    sb.Append(separator);
                }

                sb.Append(formatter(kvp.Key, kvp.Value));
                first = false;
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
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

    /// <summary>
    /// Logs session settings detection results in a standardized format.
    /// Called by dialect overrides after evaluating session settings.
    /// </summary>
    protected void LogSessionSettingsResult(in SessionSettingsResult result, string dialectName)
    {
        var snapshotParts = new string[result.Snapshot.Count];
        var i = 0;
        foreach (var kv in result.Snapshot)
        {
            snapshotParts[i++] = $"{kv.Key}={kv.Value}";
        }

        var snapshot = string.Join(", ", snapshotParts);

        if (!string.IsNullOrWhiteSpace(result.Settings))
        {
            Logger.LogInformation(
                "{Dialect} session settings detected: {CurrentSettings}. Applying changes:\n{Settings}",
                dialectName, snapshot, result.Settings);
        }
        else
        {
            Logger.LogInformation(
                "{Dialect} session settings detected: {CurrentSettings}. No changes required (already compliant)",
                dialectName, snapshot);
        }
    }

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

    private static void AppendWithEscaping(ref StringBuilderLite builder, ReadOnlySpan<char> value,
        ReadOnlySpan<char> prefix, ReadOnlySpan<char> suffix)
    {
        // SQL standard escaping: double the quote char inside the identifier.
        // We only escape the suffix char (the closer), as that's the one that breaks the string.
        // For "[" prefix, suffix is "]". For most, prefix=suffix="\"".
        var quoteChar = suffix.Length == 1 ? suffix[0] : (char)0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            builder.Append(c);
            
            if (quoteChar != 0 && c == quoteChar)
            {
                builder.Append(c); // Double it
            }
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
    /// Builds the MERGE source clause (USING ...) for MERGE-based upserts.
    /// </summary>
    public virtual string RenderMergeSource(IReadOnlyList<IColumnInfo> columns,
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

        var values = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        var names = SbLite.Create(stackalloc char[SbLite.DefaultStack]);

        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                values.Append(", ");
                names.Append(", ");
            }

            var placeholder = MakeParameterName(parameterNames[i]);
            if (columns[i].IsJsonType)
            {
                placeholder = RenderJsonArgument(placeholder, columns[i]);
            }

            values.Append(placeholder);
            names.Append(WrapObjectName(columns[i].Name));
        }

        return string.Concat("USING (VALUES (", values.ToString(), ")) AS s (", names.ToString(), ")");
    }

    /// <summary>
    /// Formats the MERGE ON clause predicate for the dialect.
    /// </summary>
    public virtual string RenderMergeOnClause(string predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return predicate;
    }

    private static void ResetProviderSpecificMetadata(DbParameter parameter)
    {
        var resetter = ProviderSpecificResetters.GetOrAdd(parameter.GetType(), BuildProviderSpecificResetter);
        resetter(parameter);
    }

    private static Action<DbParameter> BuildProviderSpecificResetter(Type parameterType)
    {
        List<ProviderPropertyReset>? resets = null;
        foreach (var propertyName in ProviderSpecificPropertyNames)
        {
            var property = parameterType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            resets ??= new List<ProviderPropertyReset>();
            var defaultValue = property.PropertyType.IsValueType
                ? Activator.CreateInstance(property.PropertyType)
                : null;
            resets.Add(new ProviderPropertyReset(property, defaultValue));
        }

        if (resets == null)
        {
            return NoopReset;
        }

        var resetArray = resets.ToArray();
        return parameter =>
        {
            foreach (var reset in resetArray)
            {
                try
                {
                    reset.Property.SetValue(parameter, reset.DefaultValue);
                }
                catch
                {
                    // Ignore provider-specific reset failures; pooled parameters should remain usable.
                }
            }
        };
    }

    private readonly struct ProviderPropertyReset
    {
        public ProviderPropertyReset(PropertyInfo property, object? defaultValue)
        {
            Property = property;
            DefaultValue = defaultValue;
        }

        public PropertyInfo Property { get; }
        public object? DefaultValue { get; }
    }

    /// <summary>
    /// Get a parameter from the pool or create a new one. For internal use by hot paths.
    /// </summary>
    private DbParameter GetPooledParameter(out bool pooled)
    {
        if (_parameterPool.TryDequeue(out var param))
        {
            pooled = true;
            // Reset pooled parameter to clean state.
            // IMPORTANT: ResetProviderSpecificMetadata must be called BEFORE ResetDbType.
            // Setting NpgsqlDbType=0 via reflection marks it as "explicitly set" internally;
            // ResetDbType() clears that flag. If called in the wrong order, Npgsql will
            // attempt to resolve NpgsqlDbType=0 and throw ArgumentOutOfRangeException.
            ResetProviderSpecificMetadata(param);
            try
            {
                param.ResetDbType();
            }
            catch
            {
                // Ignore providers that don't support ResetDbType.
            }

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
            // Clear value eagerly to avoid holding references to potentially large
            // objects (strings, byte arrays, etc.) while the parameter sits in the pool.
            parameter.Value = DBNull.Value;
            _parameterPool.Enqueue(parameter);
        }
        // If pool is full, let it get garbage collected
    }

    [SuppressMessage("Security", "cs/exposure-of-private-information",
        Justification = "This method's purpose is to store user-supplied values in DbParameters. " +
                        "No parameter values are written to logs — only timing metadata (DbType, elapsed).")]
    public virtual DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        var traceTimings = Logger.IsEnabled(LogLevel.Debug) && IsParameterTimingEnabled();
        var start = traceTimings ? Stopwatch.GetTimestamp() : 0;
        var parameter = GetPooledParameter(out var pooled);

        // Treat empty/whitespace names as null (auto-generate)
        if (string.IsNullOrWhiteSpace(name))
        {
            name = null;
        }

        // Strip dialect-specific parameter prefixes (@, :, ?, $) if present.
        // Fast path: check first char before doing any allocation. Skip entirely if clean.
        if (name != null)
        {
            if (name.Length > 0 && name[0] is '@' or ':' or '?' or '$')
            {
                name = name.TrimStart('@', ':', '?', '$');
            }

            if (!IsValidParameterName(name))
            {
                throw new ArgumentException(
                    $"Parameter name '{name}' contains invalid characters. Only alphanumeric characters and underscores are allowed.",
                    nameof(name));
            }
        }

        parameter.ParameterName = name ?? GenerateParameterName();

        // Performance: Inline null check to avoid method call overhead
        var valueIsNull = value == null || value is DBNull;

        // Resolve CLR type first (uses typeof(T) — no boxing for value types).
        // Then validate using the pre-resolved type to avoid boxing T into object?.
        var runtimeType = valueIsNull ? null : ResolveClrType(value);

        if (!valueIsNull)
        {
            // Validate CLR type compatibility with DbType before setting the value.
            // Catches mismatches early with clear messages instead of deferring to
            // provider-specific errors at execution time.
            // Pass runtimeType (already resolved) to avoid boxing value types.
            DbTypeValidator.Validate(type, runtimeType);
        }

        // Fast path: well-known primitive CLR types are never registered in AdvancedTypeRegistry.
        // Skip the IsMappedType() ConcurrentDictionary lookup for the common case.
        // PrepareParameterValue is still called — some dialects transform primitives
        // (e.g. Oracle converts Guid→string and bool→NUMBER via PrepareParameterValue).
        bool handled;
        if (runtimeType != null && s_primitiveClrTypes.Contains(runtimeType))
        {
            // Primitive fast path: skip AdvancedTypes lookup, go straight to PrepareParameterValue.
            handled = false;
        }
        else
        {
            handled = runtimeType != null &&
                      AdvancedTypes.IsMappedType(runtimeType) &&
                      AdvancedTypes.TryConfigureParameter(parameter, runtimeType, value, DatabaseType);
        }

        if (!handled)
        {
            parameter.DbType = type;
            var preparedValue = PrepareParameterValue(value, type);
            parameter.Value = preparedValue ?? DBNull.Value;
        }

        // Apply the dialect's declared Guid storage format when the caller passed DbType.Guid
        // and the AdvancedTypeRegistry did not already handle the parameter (e.g. PostgreSQL
        // sets NpgsqlDbType.Uuid via reflection — we must not overwrite that).
        if (!handled && !valueIsNull && type == DbType.Guid
                && runtimeType == typeof(Guid) && GuidFormat != GuidStorageFormat.PassThrough)
        {
            ApplyGuidFormat(parameter, (Guid)(object)value!);
        }

        // Positional providers use "?" placeholders — parameter names must be blank.
        if (!SupportsNamedParameters)
        {
            parameter.ParameterName = string.Empty;
        }

        // Apply common type coercions (Guid→string, bool→int16, DateTimeOffset→UtcDateTime).
        // Controlled by NeedsCommonConversions so dialects can opt in independently of
        // whether they use named or positional parameters.
        if (!handled && !valueIsNull && NeedsCommonConversions &&
            _commonConversions.TryGetValue(parameter.DbType, out var converter))
        {
            converter(parameter, value);
        }

        if (!valueIsNull)
        {
            if (value is string s && (parameter.DbType == DbType.String || parameter.DbType == DbType.AnsiString ||
                                      parameter.DbType == DbType.StringFixedLength ||
                                      parameter.DbType == DbType.AnsiStringFixedLength))
            {
                parameter.Size = Math.Max(s.Length, 1);
            }
        }

        // Microsoft.Data.SqlClient 6.x validates that the decimal value fits
        // within the parameter's declared Precision/Scale before sending to the
        // server.  When Precision=0, SqlClient treats the parameter as DECIMAL(1,0)
        // (the minimum valid SQL decimal), which rejects any value requiring more
        // than one significant digit (e.g. 10, 19.99, 100).
        //
        // Fix: always set Precision to at least 18 (the standard SQL Server
        // DECIMAL column precision) so any value that fits in a typical column
        // is accepted.  Scale is set to the value's natural fractional digits
        // (e.g. 2 for 19.99m, 0 for 10m) so no silent rounding occurs.
        //
        // Using Precision=18 is the industry convention (used by Dapper, EF Core).
        // All supported databases (SQL Server, PostgreSQL, Oracle, MySQL, etc.)
        // accept DECIMAL(18,S) parameters for columns declared with P≤18.
        if (!valueIsNull && parameter.DbType == DbType.Decimal && value is decimal dec)
        {
            var (inferredPrecision, inferredScale) = DecimalHelpers.Infer(dec);
            parameter.Precision = (byte)Math.Max(inferredPrecision, 18);
            parameter.Scale = (byte)inferredScale;
        }

        if (traceTimings)
        {
            var elapsedUs = TicksToMicroseconds(Stopwatch.GetTimestamp() - start);
            Logger.LogDebug(
                "DbParameter timing pooled={Pooled} dbType={DbType} handled={Handled} elapsed={ElapsedUs:0.000}us",
                pooled,
                type,
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

    public virtual DbParameter CreateDbParameter(string? name, DbType dbType, int value)
        => CreateDbParameter<int>(name, dbType, value);

    public virtual DbParameter CreateDbParameter(string? name, DbType dbType, long value)
        => CreateDbParameter<long>(name, dbType, value);

    public virtual DbParameter CreateDbParameter(string? name, DbType dbType, string value)
        => CreateDbParameter<string>(name, dbType, value);

    public virtual DbParameter CreateDbParameter(string? name, DbType dbType, Guid value)
        => CreateDbParameter<Guid>(name, dbType, value);

    public virtual DbParameter CreateDbParameter(string? name, DbType dbType, DateTime value)
        => CreateDbParameter<DateTime>(name, dbType, value);

    public virtual DbParameter CreateDbParameter(string? name, DbType dbType, DateTimeOffset value)
        => CreateDbParameter<DateTimeOffset>(name, dbType, value);


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

    public virtual string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly)
    {
        return BuildSessionSettings(GetBaseSessionSettings(), GetReadOnlySessionSettings(), readOnly);
    }

    public virtual void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        ApplyConnectionSettingsCore(connection, context, readOnly, null);
    }

    internal virtual void ApplyConnectionSettingsCore(
        IDbConnection connection,
        IDatabaseContext context,
        bool readOnly,
        string? connectionStringOverride)
    {
        var connectionString = string.IsNullOrWhiteSpace(connectionStringOverride)
            ? context.ConnectionString
            : connectionStringOverride;

        if (readOnly && !ConnectionStringHasReadOnlyParameter(connectionString))
        {
            connectionString = GetReadOnlyConnectionString(connectionString);
        }

        connection.ConnectionString = connectionString;
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

    public virtual void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
    }

    public virtual ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Returns SQL to reset the session back to read-write after a read-only transaction completes.
    /// Dialects that set session-scoped read-only state in <see cref="TryEnterReadOnlyTransaction"/>
    /// should override this to provide the corresponding reset statement.
    /// </summary>
    internal virtual string? GetReadOnlyTransactionResetSql()
    {
        return null;
    }

    /// <summary>
    /// Attempts to execute a read-only SQL statement within a transaction context.
    /// Swallows any exceptions and logs them at Debug level.
    /// Used by Oracle and MariaDB to set read-only session state.
    /// </summary>
    protected void TryExecuteReadOnlySql(ITransactionContext transaction, string sql, string dialectName)
    {
        try
        {
            if (transaction is TransactionContext tx)
            {
                tx.ExecuteSessionNonQuery(sql);
                return;
            }

            using var sc = transaction.CreateSqlContainer(sql);
            sc.ExecuteNonQueryAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to apply {DialectName} read-only session settings", dialectName);
        }
    }

    /// <summary>
    /// Async version of <see cref="TryExecuteReadOnlySql"/>.
    /// </summary>
    protected async ValueTask TryExecuteReadOnlySqlAsync(ITransactionContext transaction, string sql,
        string dialectName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (transaction is TransactionContext tx)
            {
                await tx.ExecuteSessionNonQueryAsync(sql).ConfigureAwait(false);
                return;
            }

            await using var sc = transaction.CreateSqlContainer(sql);
            await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to apply {DialectName} read-only session settings", dialectName);
        }
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

            // Enrich version string with schema DataSourceProductVersion for more accurate inference.
            // Real drivers report meaningful version strings; fakeDb returns literal "version()" which
            // lacks product markers. The schema DataSourceProductVersion (e.g. "11.7.2-MariaDB-ubu2404")
            // provides a reliable fallback.
            string? schemaProductVersion = null;
            try
            {
                var schema = connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
                if (schema.Rows.Count > 0)
                    schemaProductVersion = schema.Rows[0].Field<string>("DataSourceProductVersion");
            }
            catch
            {
                // Schema may not be available for all connections; fall back to runtime version string.
            }

            var versionForInference = !string.IsNullOrEmpty(schemaProductVersion) &&
                                      !versionString.Contains(schemaProductVersion,
                                          StringComparison.OrdinalIgnoreCase)
                ? $"{versionString} {schemaProductVersion}"
                : versionString;

            // Use dialect's virtual method first so subclasses can express their own intent.
            // If it falls through to DatabaseType (the default), fall back to the centralized
            // detection service which uses factory type names and schema metadata.
            var databaseType = InferDatabaseTypeFromInfo(productName, versionForInference);
            if (databaseType == DatabaseType)
            {
                databaseType = DatabaseDetectionService.DetectProduct(connection, Factory);
            }

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

    private bool ConnectionStringHasReadOnlyParameter(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var readOnlyParam = GetReadOnlyConnectionParameter();
        if (string.IsNullOrWhiteSpace(readOnlyParam))
        {
            return false;
        }

        return connectionString.IndexOf(readOnlyParam, StringComparison.OrdinalIgnoreCase) >= 0;
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

    /// <summary>
    /// Prepares a connection string with provider-specific settings that must be present
    /// before the DataSource is created (e.g. auto-prepare, multiplexing).
    /// Override in dialect subclasses; base is a no-op.
    /// </summary>
    internal virtual string PrepareConnectionStringForDataSource(string connectionString)
    {
        return connectionString;
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

        if (combined.Contains("tidb"))
        {
            return SupportedDatabase.TiDb;
        }

        if (combined.Contains("aurora") && combined.Contains("mysql"))
        {
            return SupportedDatabase.AuroraMySql;
        }

        if (combined.Contains("mysql"))
        {
            return SupportedDatabase.MySql;
        }

        if (combined.Contains("cockroach"))
        {
            return SupportedDatabase.CockroachDb;
        }

        if (combined.Contains("aurora") && (combined.Contains("npgsql") || combined.Contains("postgres")))
        {
            return SupportedDatabase.AuroraPostgreSql;
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
    /// Fast validation that parameter names start with a letter and contain only
    /// alphanumeric characters and underscores. This aligns with <see cref="ParameterNamePattern"/>.
    /// No parameter markers (@, :, ?, $) are allowed.
    /// </summary>
    private static bool IsValidParameterName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // First character must be [a-zA-Z] — matches ParameterNamePattern: ^[a-zA-Z][a-zA-Z0-9_]*$
        var first = name[0];
        if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z')))
        {
            return false;
        }

        // Remaining characters: alphanumeric or underscore
        for (var i = 1; i < name.Length; i++)
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

    public string GenerateParameterName()
    {
        // No modulo — uint wraps at 2^32 which is effectively collision-free in practice.
        var index = Interlocked.Increment(ref _parameterNamePoolIndex);
        return string.Concat("p", index.ToString(CultureInfo.InvariantCulture));
    }

    public string GenerateRandomName(int length, int parameterNameMaxLength)
    {
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

    public virtual object? PrepareParameterValue(object? value, DbType dbType)
    {
        return value;
    }

    /// <summary>
    /// Applies the dialect's <see cref="GuidFormat"/> to an already-created <see cref="DbParameter"/>.
    /// Called from <see cref="CreateDbParameter{T}"/> for non-handled Guid parameters whose
    /// <see cref="GuidFormat"/> is not <see cref="GuidStorageFormat.PassThrough"/>.
    /// </summary>
    private void ApplyGuidFormat(DbParameter param, Guid guid)
    {
        switch (GuidFormat)
        {
            case GuidStorageFormat.String:
                param.DbType = DbType.String;
                param.Size = 36;
                // Use string.Create to avoid heap allocation for the formatted string.
                param.Value = string.Create(36, guid, static (span, g) => g.TryFormat(span, out _, "D"));
                break;
            case GuidStorageFormat.Binary:
                param.DbType = DbType.Binary;
                param.Value = SerializeGuidAsBinary(guid);
                break;
            // PassThrough: DbType.Guid + raw Guid value are already set — nothing to do.
        }
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
    /// Gets the SQL query to retrieve the next value from a sequence.
    /// Default implementation is for Oracle; override for other sequence-supporting databases.
    /// </summary>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <returns>The SQL query text.</returns>
    public virtual string GetSequenceNextValQuery(string sequenceName)
    {
        return $"SELECT {WrapObjectName(sequenceName)}.NEXTVAL FROM DUAL";
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
                SupportedDatabase.Firebird => GeneratedKeyPlan.Returning,
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
            SupportedDatabase.Firebird => $" RETURNING {idColumnWrapped}",
            _ => string.Empty
            // Oracle is handled by OracleDialect.RenderInsertReturningClause override.
            // Oracle RETURNING INTO requires an output parameter, not an inline placeholder.
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
    internal const int FallbackMaxPoolSize = 100;
    internal virtual int DefaultMaxPoolSize => FallbackMaxPoolSize;

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
