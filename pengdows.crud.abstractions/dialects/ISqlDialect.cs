using System;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
    /// True when the dialect allows the same named parameter to appear multiple times
    /// in a single SQL statement without requiring separate parameter objects for each occurrence.
    /// Most named-parameter providers support this (SQL Server, PostgreSQL, etc.). Oracle does not.
    /// Positional-parameter providers (e.g., OleDb with ?) never support this.
    /// </summary>
    bool SupportsRepeatedNamedParameters { get; }

    /// <summary>
    /// Allows the dialect to render provider-specific JSON casts for parameter placeholders.
    /// </summary>
    /// <param name="parameterMarker">Base parameter marker (e.g., @p0).</param>
    /// <param name="column">Column metadata describing the JSON column.</param>
    /// <returns>Dialect-specific SQL fragment.</returns>
    string RenderJsonArgument(string parameterMarker, IColumnInfo column);

    /// <summary>
    /// Gives the dialect a chance to stamp provider-specific metadata on JSON parameters.
    /// </summary>
    /// <param name="parameter">Parameter instance to update.</param>
    /// <param name="column">Column metadata describing the JSON column.</param>
    void TryMarkJsonParameter(DbParameter parameter, IColumnInfo column);

    /// <summary>
    /// True when the dialect supports set-valued parameters for IN-lists.
    /// </summary>
    bool SupportsSetValuedParameters { get; }

    /// <summary>
    /// Maximum number of parameters allowed in a single command.
    /// </summary>
    int MaxParameterLimit { get; }

    /// <summary>
    /// Maximum number of rows allowed in a single multi-row INSERT statement.
    /// SQL Server: 1000; most others: no specific row limit beyond parameter count.
    /// </summary>
    int MaxRowsPerBatch { get; }

    /// <summary>
    /// Whether this dialect supports multi-row INSERT via VALUES (..., ...), (..., ...) syntax.
    /// </summary>
    bool SupportsBatchInsert { get; }

    /// <summary>
    /// Builds a multi-row INSERT statement structure for the dialect.
    /// </summary>
    /// <param name="tableName">Wrapped table name.</param>
    /// <param name="columnNames">List of wrapped column names.</param>
    /// <param name="rowCount">Number of rows in the batch.</param>
    /// <param name="query">Target query builder to write the SQL structure into.</param>
    /// <remarks>
    /// Use this to generate the dialect-specific "shape" (e.g. INSERT ALL for Oracle).
    /// The TableGateway will handle the actual parameter binding.
    /// </remarks>
    void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount, ISqlQueryBuilder query);

    /// <summary>
    /// Builds a multi-row INSERT statement structure with optional value inspection (for NULL inlining).
    /// </summary>
    void BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue);

    /// <summary>
    /// Whether this dialect supports multi-row UPDATE via optimized strategy (e.g. UPDATE FROM VALUES or MERGE).
    /// </summary>
    bool SupportsBatchUpdate { get; }

    /// <summary>
    /// Builds an optimized batch UPDATE statement structure for the dialect.
    /// </summary>
    /// <param name="tableName">Wrapped table name.</param>
    /// <param name="columnNames">List of wrapped column names (all columns to update).</param>
    /// <param name="keyColumns">List of wrapped primary key column names.</param>
    /// <param name="rowCount">Number of rows in the batch.</param>
    /// <param name="query">Target query builder to write the SQL structure into.</param>
    /// <param name="getValue">Function to get the value for a specific row and column index.</param>
    void BuildBatchUpdateSql(string tableName, IReadOnlyList<string> columnNames, IReadOnlyList<string> keyColumns, int rowCount, ISqlQueryBuilder query, Func<int, int, object?>? getValue);

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
    /// True when the dialect has permanently disabled prepare at runtime due to a server-level
    /// exhaustion error (e.g. MySQL error 1461 — <c>max_prepared_stmt_count</c> reached).
    /// Unlike <see cref="PrepareStatements"/>, this veto overrides
    /// <see cref="IDatabaseContext.ForceManualPrepare"/> because retrying after exhaustion
    /// would only compound the problem. Default implementation returns <see langword="false"/>.
    /// </summary>
    bool IsPrepareExhausted => false;

    /// <summary>
    /// Regular expression describing valid parameter names.
    /// </summary>
    Regex ParameterNamePattern { get; }

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
    /// True when the database supports DROP TABLE IF EXISTS syntax.
    /// Oracle requires a PL/SQL exception block instead.
    /// </summary>
    bool SupportsDropTableIfExists { get; }

    /// <summary>
    /// Gets the SQL statement to create a savepoint with the given name.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    /// <returns>The SQL statement (e.g., "SAVEPOINT name" or "SAVE TRANSACTION name").</returns>
    string GetSavepointSql(string name);

    /// <summary>
    /// Gets the SQL statement to rollback to a savepoint with the given name.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    /// <returns>The SQL statement (e.g., "ROLLBACK TO SAVEPOINT name" or "ROLLBACK TRANSACTION name").</returns>
    string GetRollbackToSavepointSql(string name);

    /// <summary>
    /// Indicates whether stored procedure parameter names must match exactly.
    /// </summary>
    bool RequiresStoredProcParameterNameMatch { get; }

    /// <summary>
    /// Indicates whether MERGE UPDATE SET clause requires table alias prefix on target columns.
    /// SQL Server, Oracle: true (allows `UPDATE SET t.col = value`)
    /// PostgreSQL: false (requires `UPDATE SET col = value`, will error with alias prefix)
    /// </summary>
    bool MergeUpdateRequiresTargetAlias { get; }

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
    /// Wraps a simple, single-part identifier with the dialect's quoting characters.
    /// Use this instead of <see cref="WrapObjectName"/> when the identifier is known to be
    /// a simple name with no dots or existing quotes — for example, a column name from a
    /// <c>[Column]</c> attribute or a caller-provided table alias.
    /// </summary>
    /// <param name="name">Simple identifier to wrap (no dots, no existing quotes).</param>
    /// <returns>Quoted identifier, e.g. <c>"name"</c>, <c>[name]</c>, or <c>`name`</c>.</returns>
    string WrapSimpleName(string name) => QuotePrefix + name + QuoteSuffix;

    /// <summary>
    /// Replaces neutral SQL tokens with dialect-specific quoting and parameter markers:
    /// <c>{Q}</c> → <see cref="QuotePrefix"/>, <c>{q}</c> → <see cref="QuoteSuffix"/>,
    /// <c>{S}</c> → <see cref="ParameterMarker"/>.
    /// Allows writing dialect-agnostic SQL strings without <c>TableGateway</c>.
    /// </summary>
    /// <param name="sql">SQL containing neutral tokens.</param>
    /// <returns>SQL with tokens replaced by dialect-specific characters.</returns>
    string ReplaceNeutralTokens(string sql)
    {
        if (sql == null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var qp = QuotePrefix;
        var qs = QuoteSuffix;
        var pm = ParameterMarker;
        var result = new System.Text.StringBuilder(sql.Length + 8);
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '{' && i + 2 < sql.Length && sql[i + 2] == '}')
            {
                switch (sql[i + 1])
                {
                    case 'Q': result.Append(qp); i += 2; continue;
                    case 'q': result.Append(qs); i += 2; continue;
                    case 'S': result.Append(pm); i += 2; continue;
                }
            }
            result.Append(sql[i]);
        }
        return result.ToString();
    }

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
    /// Optional alias that points to the incoming row during upsert operations.
    /// </summary>
    string? UpsertIncomingAlias { get; }

    /// <summary>
    /// Builds the MERGE source clause (USING ...) for MERGE-based upserts.
    /// </summary>
    /// <param name="columns">Columns included in the source row.</param>
    /// <param name="parameterNames">Parameter names (without markers) corresponding to columns.</param>
    /// <returns>Dialect-specific USING clause with source alias 's'.</returns>
    string RenderMergeSource(IReadOnlyList<IColumnInfo> columns, IReadOnlyList<string> parameterNames)
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

        var values = new string[columns.Count];
        var names = new string[columns.Count];

        for (var i = 0; i < columns.Count; i++)
        {
            var placeholder = MakeParameterName(parameterNames[i]);
            if (columns[i].IsJsonType)
            {
                placeholder = RenderJsonArgument(placeholder, columns[i]);
            }

            values[i] = placeholder;
            names[i] = WrapSimpleName(columns[i].Name);
        }

        return $"USING (VALUES ({string.Join(", ", values)})) AS s ({string.Join(", ", names)})";
    }

    /// <summary>
    /// Formats the MERGE ON clause predicate for the dialect.
    /// </summary>
    /// <param name="predicate">Join predicate (e.g., "t.id = s.id").</param>
    /// <returns>Dialect-specific ON clause predicate.</returns>
    string RenderMergeOnClause(string predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return predicate;
    }

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
    /// Gets the SQL query to retrieve the next value from a sequence.
    /// </summary>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <returns>The SQL query text.</returns>
    string GetSequenceNextValQuery(string sequenceName);

    /// <summary>
    /// Returns dialect-specific session settings to apply to a connection.
    /// </summary>
    /// <param name="context">The database context requesting settings.</param>
    /// <param name="readOnly">True when the session should be read-only.</param>
    /// <returns>Command text configuring session options.</returns>
    string GetConnectionSessionSettings(IDatabaseContext context, bool readOnly);


    /// <summary>
    /// Determines whether the given exception represents a unique constraint violation.
    /// </summary>
    /// <param name="ex">Exception to inspect.</param>
    /// <returns>True if the exception indicates a unique key violation.</returns>
    bool IsUniqueViolation(DbException ex);

    /// <summary>
    /// Generates a unique parameter name for the current operation.
    /// </summary>
    /// <returns>A unique parameter name (e.g., p1, p2, p42).</returns>
    string GenerateParameterName();

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

    /// <summary>
    /// Indicates whether INSERT statements support RETURNING or OUTPUT clauses for identity values.
    /// </summary>
    bool SupportsInsertReturning { get; }

    /// <summary>
    /// Generates the RETURNING or OUTPUT clause for INSERT statements to capture identity values.
    /// </summary>
    /// <param name="idColumnWrapped">
    /// The identity column name already quoted with dialect-specific identifiers
    /// (e.g., <c>"id"</c>, <c>[id]</c>, or <c>`id`</c>). Use <see cref="WrapObjectName"/>
    /// or <see cref="WrapSimpleName"/> to produce this value before calling.
    /// </param>
    /// <returns>
    /// Dialect-specific SQL fragment ready for direct concatenation into an INSERT statement,
    /// for example <c>" RETURNING &quot;id&quot;"</c> or <c>"OUTPUT INSERTED.[id]"</c>.
    /// Returns an empty string when <see cref="SupportsInsertReturning"/> is false.
    /// </returns>
    string RenderInsertReturningClause(string idColumnWrapped);

    /// <summary>
    /// Indicates whether the RETURNING/OUTPUT clause must appear before the VALUES keyword.
    /// </summary>
    bool InsertReturningClauseBeforeValues { get; }

    /// <summary>
    /// Dialect-specific limit on output/import parameters.
    /// </summary>
    int MaxOutputParameters { get; }

    /// <summary>
    /// Returns the preferred strategy for retrieving generated keys.
    /// </summary>
    GeneratedKeyPlan GetGeneratedKeyPlan();

    /// <summary>
    /// Indicates whether the dialect has a safe session-scoped last-id function.
    /// </summary>
    bool HasSessionScopedLastIdFunction();

    /// <summary>
    /// Generates a correlation token lookup query for the specified table.
    /// </summary>
    string GetCorrelationTokenLookupQuery(string tableName, string idColumnName, string correlationTokenColumn,
        string tokenParameterName);

    /// <summary>
    /// Generates a natural key lookup query for the specified table.
    /// </summary>
    string GetNaturalKeyLookupQuery(string tableName, string idColumnName, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> parameterNames);

    /// <summary>
    /// Gives the dialect a chance to transform a value before it is assigned to a parameter.
    /// Useful for databases with non-standard representations of common types.
    /// </summary>
    /// <param name="value">The raw value from the entity.</param>
    /// <param name="dbType">The target database type.</param>
    /// <returns>The transformed value to be stored in the parameter.</returns>
    object? PrepareParameterValue(object? value, DbType dbType);

    // Connection pooling properties
    /// <summary>
    /// True when the database provider supports external connection pooling.
    /// False for in-process databases like DuckDB.
    /// </summary>
    bool SupportsExternalPooling { get; }

    /// <summary>
    /// The connection string parameter name for enabling/disabling pooling.
    /// Usually "Pooling" for most providers, null if not supported.
    /// </summary>
    string? PoolingSettingName { get; }

    /// <summary>
    /// The connection string parameter name for minimum pool size.
    /// Provider-specific (e.g., "Min Pool Size" vs "MinimumPoolSize"), null if not supported.
    /// </summary>
    string? MinPoolSizeSettingName { get; }

    /// <summary>
    /// The connection string parameter name for maximum pool size.
    /// Provider-specific (e.g., "Max Pool Size" vs "MaximumPoolSize"), null if not supported.
    /// </summary>
    string? MaxPoolSizeSettingName { get; }

    /// <summary>
    /// Classifies an exception into a well-known error category for metrics and observability.
    /// </summary>
    /// <param name="exception">The exception thrown by the database operation.</param>
    /// <returns>
    /// A <see cref="DbErrorCategory"/> value indicating the type of error.
    /// Returns <see cref="DbErrorCategory.None"/> for <see cref="OperationCanceledException"/>
    /// (cancellations are tracked separately).
    /// The default implementation uses message heuristics; database-specific dialects
    /// should override this to use error codes for accurate classification.
    /// </returns>
    DbErrorCategory ClassifyException(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return DbErrorCategory.None;
        }

        var message = exception.Message;

        if (message.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
        {
            return DbErrorCategory.Deadlock;
        }

        if (message.Contains("serializ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("serialize", StringComparison.OrdinalIgnoreCase))
        {
            return DbErrorCategory.SerializationFailure;
        }

        if (message.Contains("constraint", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unique ", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("foreign key", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not-null", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("violates", StringComparison.OrdinalIgnoreCase))
        {
            return DbErrorCategory.ConstraintViolation;
        }

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return DbErrorCategory.Timeout;
        }

        return DbErrorCategory.Unknown;
    }
}
