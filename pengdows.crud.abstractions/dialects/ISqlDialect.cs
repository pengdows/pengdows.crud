using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Provides database-specific SQL dialect behaviors and capability information.
/// </summary>
public interface ISqlDialect
{
    /// <summary>
    /// Gets the detected database product information. Call DetectDatabaseInfo first.
    /// </summary>
    IDatabaseProductInfo ProductInfo { get; }

    /// <summary>
    /// Whether database info has been detected
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Type of database detected from the connected server.
    /// </summary>
    SupportedDatabase DatabaseType { get; }

    /// <summary>
    /// Marker prefix used for positional parameters.
    /// </summary>
    string ParameterMarker { get; }

    /// <summary>
    /// Retrieves the parameter marker for a specific ordinal position.
    /// </summary>
    /// <param name="ordinal">Position of the parameter in the command.</param>
    /// <returns>Properly formatted parameter marker.</returns>
    string ParameterMarkerAt(int ordinal);

    /// <summary>
    /// True when the dialect supports named parameters.
    /// </summary>
    bool SupportsNamedParameters { get; }

    /// <summary>
    /// Maximum number of parameters allowed in a single command.
    /// </summary>
    int MaxParameterLimit { get; }

    /// <summary>
    /// Maximum permitted length for parameter names.
    /// </summary>
    int ParameterNameMaxLength { get; }

    /// <summary>
    /// How stored procedure calls must be wrapped for this dialect.
    /// </summary>
    ProcWrappingStyle ProcWrappingStyle { get; }

    /// <summary>
    /// The highest SQL standard level this database/version supports.
    /// </summary>
    SqlStandardLevel MaxSupportedStandard { get; }

    /// <summary>
    /// Opening quote used for identifiers.
    /// </summary>
    string QuotePrefix { get; }

    /// <summary>
    /// Closing quote used for identifiers.
    /// </summary>
    string QuoteSuffix { get; }

    /// <summary>
    /// Separator used when composing multi-part identifiers.
    /// </summary>
    string CompositeIdentifierSeparator { get; }

    /// <summary>
    /// True when statements should be prepared prior to execution.
    /// </summary>
    bool PrepareStatements { get; }

    /// <summary>
    /// Regular expression describing valid parameter names.
    /// </summary>
    Regex ParameterNamePattern { get; }

    /// <summary>
    /// Indicates support for integrity constraints such as foreign keys.
    /// </summary>
    bool SupportsIntegrityConstraints { get; }

    /// <summary>
    /// True when the dialect supports join operations.
    /// </summary>
    bool SupportsJoins { get; }

    /// <summary>
    /// True when outer join syntax is supported.
    /// </summary>
    bool SupportsOuterJoins { get; }

    /// <summary>
    /// Indicates support for subquery expressions.
    /// </summary>
    bool SupportsSubqueries { get; }

    /// <summary>
    /// True when UNION operations are supported.
    /// </summary>
    bool SupportsUnion { get; }

    /// <summary>
    /// Indicates whether user-defined types are available.
    /// </summary>
    bool SupportsUserDefinedTypes { get; }

    /// <summary>
    /// True when array column types are supported.
    /// </summary>
    bool SupportsArrayTypes { get; }

    /// <summary>
    /// Indicates support for regular expression functions.
    /// </summary>
    bool SupportsRegularExpressions { get; }

    /// <summary>
    /// True when the dialect supports the SQL MERGE statement.
    /// </summary>
    bool SupportsMerge { get; }

    /// <summary>
    /// Indicates native XML type support.
    /// </summary>
    bool SupportsXmlTypes { get; }

    /// <summary>
    /// True when window function syntax is available.
    /// </summary>
    bool SupportsWindowFunctions { get; }

    /// <summary>
    /// Indicates support for common table expressions.
    /// </summary>
    bool SupportsCommonTableExpressions { get; }

    /// <summary>
    /// True when INSTEAD OF triggers are supported.
    /// </summary>
    bool SupportsInsteadOfTriggers { get; }

    /// <summary>
    /// Indicates support for TRUNCATE TABLE commands.
    /// </summary>
    bool SupportsTruncateTable { get; }

    /// <summary>
    /// True when temporal table features are available.
    /// </summary>
    bool SupportsTemporalData { get; }

    /// <summary>
    /// Indicates enhanced window function support.
    /// </summary>
    bool SupportsEnhancedWindowFunctions { get; }

    /// <summary>
    /// True when native JSON column types are supported.
    /// </summary>
    bool SupportsJsonTypes { get; }

    /// <summary>
    /// Indicates support for ROW PATTERN MATCHING features.
    /// </summary>
    bool SupportsRowPatternMatching { get; }

    /// <summary>
    /// True when multidimensional array types are supported.
    /// </summary>
    bool SupportsMultidimensionalArrays { get; }

    /// <summary>
    /// Indicates support for property graph queries.
    /// </summary>
    bool SupportsPropertyGraphQueries { get; }

    // PostgreSQL and modern SQL feature gates
    /// <summary>
    /// Indicates support for SQL/JSON constructors and functions per newer SQL standards.
    /// </summary>
    bool SupportsSqlJsonConstructors { get; }

    /// <summary>
    /// Indicates support for JSON_TABLE or equivalent SQL/JSON table mapping features.
    /// </summary>
    bool SupportsJsonTable { get; }

    /// <summary>
    /// Indicates MERGE supports RETURNING (or equivalent) to fetch affected rows.
    /// </summary>
    bool SupportsMergeReturning { get; }

    /// <summary>
    /// True when INSERT ... ON CONFLICT syntax is available.
    /// </summary>
    bool SupportsInsertOnConflict { get; }

    /// <summary>
    /// Indicates support for ON DUPLICATE KEY syntax.
    /// </summary>
    bool SupportsOnDuplicateKey { get; }

    /// <summary>
    /// True when savepoint statements are supported.
    /// </summary>
    bool SupportsSavepoints { get; }

    /// <summary>
    /// Indicates whether stored procedure parameter names must match exactly.
    /// </summary>
    bool RequiresStoredProcParameterNameMatch { get; }

    /// <summary>
    /// True when the dialect supports namespaces or schemas.
    /// </summary>
    bool SupportsNamespaces { get; }

    /// <summary>
    /// True when this dialect acts as a fallback with limited capabilities.
    /// </summary>
    bool IsFallbackDialect { get; }

    /// <summary>
    /// Provides a human-readable warning describing compatibility issues.
    /// </summary>
    /// <returns>Warning message for the current dialect.</returns>
    string GetCompatibilityWarning();

    /// <summary>
    /// Indicates whether modern SQL features can be used safely.
    /// </summary>
    bool CanUseModernFeatures { get; }

    /// <summary>
    /// True when the dialect has basic compatibility with CRUD operations.
    /// </summary>
    bool HasBasicCompatibility { get; }

    /// <summary>
    /// Wraps an object name with the dialect's quoting strategy.
    /// </summary>
    /// <param name="name">Name to wrap.</param>
    /// <returns>Quoted identifier.</returns>
    string WrapObjectName(string name);

    /// <summary>
    /// Formats a parameter name according to dialect rules.
    /// </summary>
    /// <param name="parameterName">Unformatted parameter name.</param>
    /// <returns>Formatted parameter name.</returns>
    string MakeParameterName(string parameterName);

    /// <summary>
    /// Formats a parameter name based on the provided parameter object.
    /// </summary>
    /// <param name="dbParameter">Parameter to name.</param>
    /// <returns>Formatted parameter name.</returns>
    string MakeParameterName(DbParameter dbParameter);

    /// <summary>
    /// Produces the column expression used for upsert operations.
    /// </summary>
    /// <param name="columnName">Column being inserted or updated.</param>
    /// <returns>Column expression respecting dialect conventions.</returns>
    string UpsertIncomingColumn(string columnName);

    /// <summary>
    /// Creates a parameter with the specified name, type, and value.
    /// </summary>
    /// <typeparam name="T">Parameter value type.</typeparam>
    /// <param name="name">Parameter name or null for positional parameters.</param>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">Value to assign.</param>
    /// <returns>Configured database parameter.</returns>
    DbParameter CreateDbParameter<T>(string? name, DbType type, T value);

    /// <summary>
    /// Creates a parameter without specifying a name.
    /// </summary>
    /// <typeparam name="T">Parameter value type.</typeparam>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">Value to assign.</param>
    /// <returns>Configured database parameter.</returns>
    DbParameter CreateDbParameter<T>(DbType type, T value);

    /// <summary>
    /// Returns the query used to fetch the database version.
    /// </summary>
    /// <returns>SQL text to retrieve version information.</returns>
    string GetVersionQuery();

    /// <summary>
    /// Reads the database version from the connection.
    /// </summary>
    /// <param name="connection">Connection to inspect.</param>
    /// <returns>Version string reported by the database.</returns>
    string GetDatabaseVersion(ITrackedConnection connection);

    /// <summary>
    /// Retrieves the data source information schema as a <see cref="DataTable"/>.
    /// </summary>
    /// <param name="connection">Connection to query.</param>
    /// <returns>Schema information.</returns>
    DataTable GetDataSourceInformationSchema(ITrackedConnection connection);

    /// <summary>
    /// Returns dialect-specific session settings to apply to a connection.
    /// </summary>
    /// <param name="context">The database context requesting settings.</param>
    /// <param name="readOnly">True when the session should be read-only.</param>
    /// <returns>Command text configuring session options.</returns>
    string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly);

    /// <summary>
    /// Legacy accessor for session settings without context information.
    /// </summary>
    /// <returns>Command text configuring session options.</returns>
    [Obsolete("Use the overload accepting context and readOnly.")]
    string GetConnectionSessionSettings();

    /// <summary>
    /// Applies connection-string or provider-specific settings to the provided connection.
    /// </summary>
    /// <param name="connection">Connection to configure.</param>
    /// <param name="context">Database context requesting the settings.</param>
    /// <param name="readOnly">True when the connection should be read-only.</param>
    void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly);

    /// <summary>
    /// Legacy overload for connection settings without context information.
    /// </summary>
    /// <param name="connection">Connection to configure.</param>
    [Obsolete("Use the overload accepting context and readOnly.")]
    void ApplyConnectionSettings(IDbConnection connection);

    /// <summary>
    /// Attempts to enter a read-only transaction. Implementations may be a no-op.
    /// </summary>
    /// <param name="transaction">Transaction context.</param>
    void TryEnterReadOnlyTransaction(ITransactionContext transaction);

    /// <summary>
    /// Determines whether READ_COMMITTED_SNAPSHOT is enabled.
    /// </summary>
    /// <param name="connection">Connection to check.</param>
    /// <returns>True if the snapshot isolation is on.</returns>
    bool IsReadCommittedSnapshotOn(ITrackedConnection connection);

    /// <summary>
    /// Determines whether the given exception represents a unique constraint violation.
    /// </summary>
    /// <param name="ex">Exception to inspect.</param>
    /// <returns>True if the exception indicates a unique key violation.</returns>
    bool IsUniqueViolation(DbException ex);

    /// <summary>
    /// Detects database product information from the connection.
    /// </summary>
    /// <param name="connection">Connection to interrogate.</param>
    /// <returns>Discovered product information.</returns>
    Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection);

    /// <summary>
    /// Synchronously detects database product information from the connection.
    /// </summary>
    /// <param name="connection">Connection to interrogate.</param>
    /// <returns>Discovered product information.</returns>
    IDatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection);

    /// <summary>
    /// Parses a raw version string into a <see cref="Version"/> instance.
    /// </summary>
    /// <param name="versionString">Version string reported by the database.</param>
    /// <returns>Parsed version or null when parsing fails.</returns>
    Version? ParseVersion(string versionString);

    /// <summary>
    /// Retrieves the major version number from the version string.
    /// </summary>
    /// <param name="versionString">Version string reported by the database.</param>
    /// <returns>Major version or null if unavailable.</returns>
    int? GetMajorVersion(string versionString);

    /// <summary>
    /// Generates a random identifier respecting name length limits.
    /// </summary>
    /// <param name="length">Requested length of the name.</param>
    /// <param name="parameterNameMaxLength">Maximum allowed name length.</param>
    /// <returns>Randomly generated name.</returns>
    string GenerateRandomName(int length, int parameterNameMaxLength);

    /// <summary>
    /// Gets the database-specific query for retrieving the last inserted identity value.
    /// </summary>
    /// <returns>SQL query to get the last inserted identity value, or empty string if not supported.</returns>
    string GetLastInsertedIdQuery();
}
