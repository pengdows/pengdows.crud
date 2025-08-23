using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

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

    SupportedDatabase DatabaseType { get; }
    string ParameterMarker { get; }
    bool SupportsNamedParameters { get; }
    int MaxParameterLimit { get; }
    int ParameterNameMaxLength { get; }
    ProcWrappingStyle ProcWrappingStyle { get; }

    /// <summary>
    /// The highest SQL standard level this database/version supports
    /// </summary>
    SqlStandardLevel MaxSupportedStandard { get; }

    string QuotePrefix { get; }
    string QuoteSuffix { get; }
    string CompositeIdentifierSeparator { get; }
    bool PrepareStatements { get; }
    Regex ParameterNamePattern { get; }
    bool SupportsIntegrityConstraints { get; }
    bool SupportsJoins { get; }
    bool SupportsOuterJoins { get; }
    bool SupportsSubqueries { get; }
    bool SupportsUnion { get; }
    bool SupportsUserDefinedTypes { get; }
    bool SupportsArrayTypes { get; }
    bool SupportsRegularExpressions { get; }
    bool SupportsMerge { get; }
    bool SupportsXmlTypes { get; }
    bool SupportsWindowFunctions { get; }
    bool SupportsCommonTableExpressions { get; }
    bool SupportsInsteadOfTriggers { get; }
    bool SupportsTruncateTable { get; }
    bool SupportsTemporalData { get; }
    bool SupportsEnhancedWindowFunctions { get; }
    bool SupportsJsonTypes { get; }
    bool SupportsRowPatternMatching { get; }
    bool SupportsMultidimensionalArrays { get; }
    bool SupportsPropertyGraphQueries { get; }
    bool SupportsInsertOnConflict { get; }
    bool SupportsOnDuplicateKey { get; }
    bool RequiresStoredProcParameterNameMatch { get; }
    bool SupportsNamespaces { get; }
    bool IsFallbackDialect { get; }
    string GetCompatibilityWarning();
    bool CanUseModernFeatures { get; }
    bool HasBasicCompatibility { get; }
    string WrapObjectName(string name);
    string MakeParameterName(string parameterName);
    string MakeParameterName(DbParameter dbParameter);
    DbParameter CreateDbParameter<T>(string? name, DbType type, T value);
    DbParameter CreateDbParameter<T>(DbType type, T value);
    string GetVersionQuery();
    string GetDatabaseVersion(ITrackedConnection connection);
    DataTable GetDataSourceInformationSchema(ITrackedConnection connection);
    string GetConnectionSessionSettings();
    void ApplyConnectionSettings(IDbConnection connection);
    bool IsReadCommittedSnapshotOn(ITrackedConnection connection);

    /// <summary>
    /// Detects database product information from the connection
    /// </summary>
    Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection);

    IDatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection);
    Version? ParseVersion(string versionString);
    int? GetMajorVersion(string versionString);
    string GenerateRandomName(int length, int parameterNameMaxLength);
}