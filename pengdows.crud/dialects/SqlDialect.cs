using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Base SQL dialect implementing standard SQL behaviors with feature detection
/// </summary>
public abstract class SqlDialect:ISqlDialect
{
    protected readonly DbProviderFactory Factory;
    protected readonly ILogger Logger;
    protected DbConnectionStringBuilder ConnectionStringBuilder { get; init; }
    private IDatabaseProductInfo? _productInfo;

    protected SqlDialect(DbProviderFactory factory, ILogger logger)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ConnectionStringBuilder = Factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
    }

    /// <summary>
    /// Gets the detected database product information. Call DetectDatabaseInfo first.
    /// </summary>
    public IDatabaseProductInfo ProductInfo => _productInfo ?? throw new InvalidOperationException("Database info not detected. Call DetectDatabaseInfo first.");

    /// <summary>
    /// Whether database info has been detected
    /// </summary>
    public bool IsInitialized => _productInfo != null;

    // Core properties with SQL-92 defaults; override for database-specific behavior
    public abstract SupportedDatabase DatabaseType { get; }
    public virtual string ParameterMarker => "?";
    public virtual string ParameterMarkerAt(int ordinal) => ParameterMarker;
    public virtual bool SupportsNamedParameters => true;
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
    public virtual string QuotePrefix => "\"";  // SQL-92 standard
    public virtual string QuoteSuffix => "\"";   // SQL-92 standard
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
    public virtual bool RequiresStoredProcParameterNameMatch => false;
    public virtual bool SupportsNamespaces => false; // SQL-92 does not require schema support

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
    /// Indicates whether SQL:2003 or later features may be used.
    /// </summary>
    public bool CanUseModernFeatures => MaxSupportedStandard >= SqlStandardLevel.Sql2003;

    /// <summary>
    /// Indicates whether the database meets SQL-92 compatibility.
    /// </summary>
    public bool HasBasicCompatibility => MaxSupportedStandard >= SqlStandardLevel.Sql92;

    public virtual string WrapObjectName(string name)
    {
        if (string.IsNullOrEmpty(name?.Trim()))
        {
            return string.Empty;
        }

        var cleaned = name.Replace(QuotePrefix, string.Empty).Replace(QuoteSuffix, string.Empty);
        var parts = cleaned
            .Split(CompositeIdentifierSeparator)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
        var sb = new StringBuilder();

        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(CompositeIdentifierSeparator);
            }

            sb.Append(QuotePrefix).Append(parts[i]).Append(QuoteSuffix);
        }

        return sb.ToString();
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
        return $"EXCLUDED.{WrapObjectName(columnName)}";
    }

    public virtual DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        var p = Factory.CreateParameter() ?? throw new InvalidOperationException("Failed to create parameter.");

        if (string.IsNullOrWhiteSpace(name))
        {
            name = GenerateRandomName(5, ParameterNameMaxLength);
        }
        else if (SupportsNamedParameters)
        {
            // Trim any leading marker characters from provided names so
            // DbParameter.ParameterName is the raw identifier the provider expects.
            // Some providers (e.g., DuckDB) use '$' in SQL text but expect the
            // underlying parameter name without the marker.
            name = name.TrimStart('@', ':', '?', '$');
        }

        var valueIsNull = Utils.IsNullOrDbNull(value);
        p.ParameterName = name;
        p.DbType = type;
        p.Value = valueIsNull ? DBNull.Value : value!;

        if (!SupportsNamedParameters)
        {
            p.ParameterName = string.Empty;

            if (!valueIsNull)
            {
                switch (p.DbType)
                {
                    case DbType.Guid:
                        p.DbType = DbType.String;
                        if (value is Guid guidValue)
                        {
                            p.Value = guidValue.ToString();
                            p.Size = 36;
                        }
                        break;
                    case DbType.Boolean:
                        p.DbType = DbType.Int16;
                        if (value is bool boolValue)
                        {
                            p.Value = boolValue ? (short)1 : (short)0;
                        }
                        break;
                    case DbType.DateTimeOffset:
                        p.DbType = DbType.DateTime;
                        if (value is DateTimeOffset dtoValue)
                        {
                            p.Value = dtoValue.DateTime;
                        }
                        break;
                }
            }
        }

        if (!valueIsNull)
        {
            switch (p.DbType)
            {
                case DbType.String:
                case DbType.AnsiString:
                case DbType.StringFixedLength:
                case DbType.AnsiStringFixedLength:
                    if (value is string s)
                    {
                        p.Size = Math.Max(s.Length, 1);
                    }
                    break;
                case DbType.Decimal when value is decimal dec:
                {
                    var (prec, scale) = DecimalHelpers.Infer(dec);
                    p.Precision = (byte)prec;
                    p.Scale = (byte)scale;
                    break;
                }
            }
        }

        return p;
    }

    public virtual DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return CreateDbParameter(null, type, value);
    }

    // Methods for database-specific operations
    public virtual string GetVersionQuery() => "SELECT 'SQL-92 Compatible Database' AS version";

    public virtual string GetDatabaseVersion(ITrackedConnection connection)
    {
        return GetDatabaseVersionAsync(connection).GetAwaiter().GetResult();
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

            Logger.LogInformation("Detected database: {ProductName} {Version} (SQL Standard: {Standard})", productName, versionString, standardCompliance);
            return _productInfo;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to detect database information");

            _productInfo = new DatabaseProductInfo
            {
                ProductName = "Unknown Database",
                ProductVersion = "Unknown Version",
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
    protected virtual string GetBaseSessionSettings()
    {
        return string.Empty;
    }

    /// <summary>
    /// Gets the read-only specific session settings. Override to provide database-specific read-only settings.
    /// </summary>
    /// <returns>Read-only session settings SQL string</returns>
    protected virtual string GetReadOnlySessionSettings()
    {
        return string.Empty;
    }

    /// <summary>
    /// Gets the connection string parameter for read-only mode. Override to provide database-specific parameter.
    /// </summary>
    /// <returns>Connection string parameter for read-only mode, or null if not supported</returns>
    protected virtual string? GetReadOnlyConnectionParameter()
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
            return false;
            
        var v = ProductInfo.ParsedVersion;
        return v.Major > major || 
               (v.Major == major && v.Minor > minor) || 
               (v.Major == major && v.Minor == minor && v.Build >= build);
    }

    /// <summary>
    /// Gets a mapping of major versions to SQL standard levels. Override in derived classes.
    /// </summary>
    /// <returns>Dictionary mapping major version numbers to standard compliance levels</returns>
    protected virtual Dictionary<int, SqlStandardLevel> GetMajorVersionToStandardMapping()
    {
        return new Dictionary<int, SqlStandardLevel>();
    }

    /// <summary>
    /// Gets the default SQL standard level when version information is unavailable
    /// </summary>
    /// <returns>Default SQL standard level</returns>
    protected virtual SqlStandardLevel GetDefaultStandardLevel()
    {
        return SqlStandardLevel.Sql92;
    }

    /// <summary>
    /// Hook for database-specific connection configuration. Override to provide custom logic.
    /// </summary>
    /// <param name="connection">Database connection to configure</param>
    /// <param name="context">Database context</param>
    /// <param name="readOnly">Whether this is a read-only connection</param>
    protected virtual void ConfigureProviderSpecificSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        // Default implementation does nothing - override in derived classes
    }

    #endregion

    /// <summary>
    /// Determines SQL standard compliance based on database version.
    /// Default implementation uses version mapping from GetMajorVersionToStandardMapping().
    /// Override for complex version logic.
    /// </summary>
    protected virtual SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
            return GetDefaultStandardLevel();
        
        var mapping = GetMajorVersionToStandardMapping();
        if (mapping.Count == 0)
            return GetDefaultStandardLevel();
        
        // Find the highest version that the current version meets or exceeds
        var applicableVersions = mapping.Where(kvp => version.Major >= kvp.Key)
                                       .OrderByDescending(kvp => kvp.Key);
                                       
        return applicableVersions.FirstOrDefault().Value;
    }

    public virtual IDatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection)
    {
        return DetectDatabaseInfoAsync(connection).GetAwaiter().GetResult();
    }

    protected virtual async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        var versionQueries = new[]
            {
                GetVersionQuery(),
                "SELECT CURRENT_VERSION",
                "SELECT version()",
                "SELECT @@version",
                "SELECT * FROM v$version WHERE rownum = 1"
            }
            .Where(q => !string.IsNullOrWhiteSpace(q));

        Exception? lastException = null;
        foreach (var query in versionQueries)
        {
            try
            {
                await using var cmd = (DbCommand)connection.CreateCommand();
                cmd.CommandText = query;
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                if (result != null && !string.IsNullOrEmpty(result.ToString()))
                {
                    return result.ToString()!;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        if (lastException != null)
        {
            return $"Error retrieving version: {lastException.Message}";
        }

        return "Unknown Version (SQL-92 Compatible)";
    }

    protected virtual Task<string?> GetProductNameAsync(ITrackedConnection connection)
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

    protected virtual string ExtractProductNameFromVersion(string versionString)
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

        return null;
    }

    public virtual int? GetMajorVersion(string versionString)
    {
        return ParseVersion(versionString)?.Major;
    }

    public string GenerateRandomName(int length, int parameterNameMaxLength)
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();
        var len = Math.Min(Math.Max(length, 2), parameterNameMaxLength);

        Span<char> buffer = stackalloc char[len];
        const int firstCharMax = 52; // a-zA-Z
        var anyOtherMax = validChars.Length;

        buffer[0] = validChars[Random.Shared.Next(firstCharMax)];
        for (var i = 1; i < len; i++)
        {
            buffer[i] = validChars[Random.Shared.Next(anyOtherMax)];
        }

        return new string(buffer);
    }

    /// <summary>
    /// Gets the database-specific query for retrieving the last inserted identity value.
    /// Base implementation returns empty string. Override in dialect-specific classes.
    /// </summary>
    /// <returns>SQL query to get the last inserted identity value, or empty string if not supported.</returns>
    public virtual string GetLastInsertedIdQuery()
    {
        return DatabaseType switch
        {
            SupportedDatabase.Sqlite => "SELECT last_insert_rowid()",
            SupportedDatabase.SqlServer => "SELECT SCOPE_IDENTITY()",
            SupportedDatabase.MySql => "SELECT LAST_INSERT_ID()",
            SupportedDatabase.PostgreSql => "SELECT lastval()",
            SupportedDatabase.Oracle => "SELECT @@IDENTITY",
            _ => string.Empty
        };
    }

    // ---- Legacy utility helpers (kept for test compatibility) ----
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

        var candidate = (min % 2 == 0) ? min + 1 : min;
        while (!IsPrime(candidate))
        {
            candidate += 2;
        }
        return candidate;
    }
}
