using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud;

public class DataSourceInformation : IDataSourceInformation
{
    private readonly SqlDialect _dialect;
    private int? _maxOutputParameters;

    public DataSourceInformation(SqlDialect dialect)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        var info = dialect.ProductInfo;
        DatabaseProductName = info.ProductName;
        DatabaseProductVersion = info.ProductVersion;
        ParsedVersion = info.ParsedVersion;
        Product = info.DatabaseType;
        StandardCompliance = info.StandardCompliance;
        ParameterMarkerPattern = string.Empty;
        ParameterNamePatternRegex = dialect.ParameterNamePattern;
    }

    public string DatabaseProductName { get; }
    public string DatabaseProductVersion { get; }
    public Version? ParsedVersion { get; private set; }
    public SupportedDatabase Product { get; }
    public SqlStandardLevel StandardCompliance { get; }
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

    public static DataSourceInformation Create(ITrackedConnection connection, DbProviderFactory factory, ILoggerFactory? loggerFactory = null)
    {
        return CreateAsync(connection, factory, loggerFactory).GetAwaiter().GetResult();
    }

    public static async Task<DataSourceInformation> CreateAsync(ITrackedConnection connection, DbProviderFactory factory, ILoggerFactory? loggerFactory = null)
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

        var dialect = await SqlDialectFactory.CreateDialectAsync(connection, factory, loggerFactory).ConfigureAwait(false);
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
