#region

using System.Data;
using System.Text.RegularExpressions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Provides metadata about the connected database and its capabilities.
/// </summary>
public interface IDataSourceInformation
{
    /// <summary>
    /// Gets the regular expression pattern describing how parameter markers are formatted.
    /// </summary>
    string ParameterMarkerPattern { get; }

    /// <summary>
    /// Gets the string used to prefix quoted identifiers in SQL statements.
    /// </summary>
    string QuotePrefix { get; }

    /// <summary>
    /// Gets the string used to suffix quoted identifiers in SQL statements.
    /// </summary>
    string QuoteSuffix { get; }

    /// <summary>
    /// Gets a value indicating whether the database supports named parameters.
    /// </summary>
    bool SupportsNamedParameters { get; }

    /// <summary>
    /// Gets the character or string used to denote parameters in SQL commands.
    /// </summary>
    string ParameterMarker { get; }

    /// <summary>
    /// Gets the maximum length of a named parameter.
    /// </summary>
    int ParameterNameMaxLength { get; }

    /// <summary>
    /// Gets the regular expression used to validate parameter names.
    /// </summary>
    Regex ParameterNamePatternRegex { get; }

    /// <summary>
    /// Gets the friendly name of the database product.
    /// </summary>
    string DatabaseProductName { get; }

    /// <summary>
    /// Gets the reported version string of the database product.
    /// </summary>
    string DatabaseProductVersion { get; }

    /// <summary>
    /// Gets the separator used when quoting composite identifiers (e.g., schema.table).
    /// </summary>
    string CompositeIdentifierSeparator { get; }

    /// <summary>
    /// Gets a value indicating whether prepared statements should be used when available.
    /// </summary>
    bool PrepareStatements { get; }

    /// <summary>
    /// Gets the wrapping style to use when invoking stored procedures.
    /// </summary>
    ProcWrappingStyle ProcWrappingStyle { get; }

    /// <summary>
    /// Gets the maximum number of parameters allowed in a single command.
    /// </summary>
    int MaxParameterLimit { get; }

    /// <summary>
    /// Gets the maximum number of output parameters supported in a command.
    /// </summary>
    int MaxOutputParameters { get; }

    /// <summary>
    /// Gets the detected database product as an enumeration value.
    /// </summary>
    SupportedDatabase Product { get; }

    /// <summary>
    /// Gets a value indicating whether the database supports MERGE statements.
    /// </summary>
    bool SupportsMerge { get; }

    /// <summary>
    /// Gets a value indicating whether the database supports INSERT ... ON CONFLICT semantics.
    /// </summary>
    bool SupportsInsertOnConflict { get; }

    /// <summary>
    /// Indicates whether stored procedure parameter names must match the declared names in the database.
    /// This is true for Oracle, PostgreSQL, and CockroachDB when using named binding.
    /// </summary>
    bool RequiresStoredProcParameterNameMatch { get; }

    /// <summary>
    /// Retrieves the raw database version string from the specified connection.
    /// </summary>
    /// <param name="connection">The connection to query.</param>
    /// <returns>The version string reported by the database.</returns>
    string GetDatabaseVersion(ITrackedConnection connection);

    /// <summary>
    /// Retrieves the data source information schema for the specified connection.
    /// </summary>
    /// <param name="connection">The connection to query.</param>
    /// <returns>A <see cref="DataTable"/> containing the information schema.</returns>
    DataTable GetSchema(ITrackedConnection connection);
}
