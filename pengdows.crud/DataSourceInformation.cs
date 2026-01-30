// =============================================================================
// FILE: DataSourceInformation.cs
// PURPOSE: Provides metadata about a database connection's capabilities,
//          including version info, parameter formats, and feature support.
//
// AI SUMMARY:
// - Wraps an ISqlDialect to expose database metadata in a read-only fashion.
// - Created during DatabaseContext initialization to capture:
//   * Product name and version (e.g., "PostgreSQL 14.2")
//   * Parameter marker format (@, :, ?)
//   * Identifier quoting (", [], `)
//   * Feature support (MERGE, ON CONFLICT, etc.)
//   * Max parameter limits
// - The CreateAsync factory method connects to the database and auto-detects
//   the dialect based on provider type and server version.
// - Used by SqlContainer and TableGateway to generate database-appropriate SQL.
// - Exposes compatibility warnings for unsupported/fallback dialects.
// - BuildEmptySchema() creates a DataTable matching ADO.NET schema format
//   for testing and compatibility scenarios.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// Provides metadata about a database connection's capabilities and SQL dialect.
/// </summary>
/// <remarks>
/// <para>
/// This class exposes information about the connected database including its product
/// name, version, parameter format, identifier quoting rules, and supported features
/// like MERGE statements or upsert syntax.
/// </para>
/// <para>
/// <strong>Creation:</strong> Typically created automatically by <see cref="DatabaseContext"/>
/// during initialization, or manually via <see cref="CreateAsync"/>.
/// </para>
/// <para>
/// <strong>Thread Safety:</strong> This class is immutable after construction and safe
/// for concurrent access.
/// </para>
/// </remarks>
/// <seealso cref="IDataSourceInformation"/>
/// <seealso cref="ISqlDialect"/>
/// <seealso cref="DatabaseContext"/>
public class DataSourceInformation : IDataSourceInformation
{
    private readonly ISqlDialect _dialect;
    private int? _maxOutputParameters;

    /// <summary>
    /// Initializes a new instance from an existing dialect.
    /// </summary>
    /// <param name="dialect">The SQL dialect providing database-specific information.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="dialect"/> is null.</exception>
    internal DataSourceInformation(ISqlDialect dialect)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        var info = dialect.IsInitialized
            ? dialect.ProductInfo
            : new DatabaseProductInfo
            {
                ProductName = "Unknown",
                ProductVersion = string.Empty,
                DatabaseType = dialect.DatabaseType,
                StandardCompliance = SqlStandardLevel.Sql92
            };
        DatabaseProductName = info.ProductName;
        DatabaseProductVersion = info.ProductVersion;
        ParsedVersion = info.ParsedVersion;
        Product = info.DatabaseType;
        StandardCompliance = info.StandardCompliance;
        ParameterMarkerPattern = string.Empty;
        ParameterNamePatternRegex = dialect.ParameterNamePattern;
    }

    /// <inheritdoc />
    public string DatabaseProductName { get; }

    /// <inheritdoc />
    public string DatabaseProductVersion { get; }

    /// <inheritdoc />
    public Version? ParsedVersion { get; private set; }

    /// <inheritdoc />
    public SupportedDatabase Product { get; }

    /// <inheritdoc />
    public SqlStandardLevel StandardCompliance { get; }

    /// <inheritdoc />
    public string ParameterMarkerPattern { get; }
    public string QuotePrefix => _dialect.QuotePrefix;
    public string QuoteSuffix => _dialect.QuoteSuffix;
    public bool SupportsNamedParameters => _dialect.SupportsNamedParameters;
    public string ParameterMarker => _dialect.ParameterMarker;
    public int ParameterNameMaxLength => _dialect.ParameterNameMaxLength;
    public Regex ParameterNamePatternRegex { get; }
    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;
    public bool PrepareStatements => _dialect.PrepareStatements;
    public ProcWrappingStyle ProcWrappingStyle => _dialect.ProcWrappingStyle;
    public int MaxParameterLimit => _dialect.MaxParameterLimit;

    public int MaxOutputParameters
    {
        get => _maxOutputParameters ?? _dialect.MaxOutputParameters;
        set => _maxOutputParameters = value;
    }

    public bool SupportsMerge => _dialect.SupportsMerge;
    public bool SupportsInsertOnConflict => _dialect.SupportsInsertOnConflict;
    public bool SupportsOnDuplicateKey => _dialect.SupportsOnDuplicateKey;
    public bool RequiresStoredProcParameterNameMatch => _dialect.RequiresStoredProcParameterNameMatch;
    public bool IsUsingFallbackDialect => _dialect.IsFallbackDialect;

    public string GetCompatibilityWarning()
    {
        return _dialect.GetCompatibilityWarning();
    }

    public bool CanUseModernFeatures => _dialect.CanUseModernFeatures;
    public bool HasBasicCompatibility => _dialect.HasBasicCompatibility;

    public DataTable GetSchema(ITrackedConnection connection)
    {
        return _dialect.GetDataSourceInformationSchema(connection);
    }

    public static DataSourceInformation Create(ITrackedConnection connection, DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null)
    {
        return CreateAsync(connection, factory, loggerFactory).GetAwaiter().GetResult();
    }

    public static async Task<DataSourceInformation> CreateAsync(ITrackedConnection connection,
        DbProviderFactory factory, ILoggerFactory? loggerFactory = null)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        loggerFactory ??= NullLoggerFactory.Instance;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync().ConfigureAwait(false);
        }

        var dialect = await SqlDialectFactory.CreateDialectAsync(connection, factory, loggerFactory)
            .ConfigureAwait(false);
        return new DataSourceInformation(dialect);
    }

    public static DataTable BuildEmptySchema(
        string productName,
        string productVersion,
        string parameterMarkerPattern,
        string parameterMarkerFormat,
        int parameterNameMaxLength,
        string parameterNamePattern,
        string parameterNamePatternRegex,
        bool supportsNamedParameters)
    {
        var dt = new DataTable();
        dt.Columns.Add("DataSourceProductName", typeof(string));
        dt.Columns.Add("DataSourceProductVersion", typeof(string));
        dt.Columns.Add("ParameterMarkerPattern", typeof(string));
        dt.Columns.Add("ParameterMarkerFormat", typeof(string));
        dt.Columns.Add("ParameterNameMaxLength", typeof(int));
        dt.Columns.Add("ParameterNamePattern", typeof(string));
        dt.Columns.Add("ParameterNamePatternRegex", typeof(string));
        dt.Columns.Add("SupportsNamedParameters", typeof(bool));

        dt.Rows.Add(
            productName,
            productVersion,
            parameterMarkerPattern,
            parameterMarkerFormat,
            parameterNameMaxLength,
            parameterNamePattern,
            parameterNamePatternRegex,
            supportsNamedParameters
        );

        return dt;
    }
}