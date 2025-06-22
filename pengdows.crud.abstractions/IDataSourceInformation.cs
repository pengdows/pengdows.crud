#region

using System.Data;
using System.Text.RegularExpressions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public interface IDataSourceInformation
{
    string ParameterMarkerPattern { get; }
    string QuotePrefix { get; }
    string QuoteSuffix { get; }
    bool SupportsNamedParameters { get; }
    string ParameterMarker { get; }
    int ParameterNameMaxLength { get; }
    Regex ParameterNamePatternRegex { get; }
    string DatabaseProductName { get; }
    string DatabaseProductVersion { get; }
    string CompositeIdentifierSeparator { get; }
    bool PrepareStatements { get; }
    ProcWrappingStyle ProcWrappingStyle { get; }
    int MaxParameterLimit { get; }
    SupportedDatabase Product { get; }
    bool SupportsMerge { get; }
    bool SupportsInsertOnConflict { get; }

    /// <summary>
    /// Indicates whether stored procedure parameter names must match the declared names in the database.
    /// This is true for Oracle, PostgreSQL, and CockroachDB when using named binding.
    /// </summary>
    bool RequiresStoredProcParameterNameMatch { get; }

    string GetDatabaseVersion(ITrackedConnection connection);
    DataTable GetSchema(ITrackedConnection connection);
}