using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// SQL-92 compliant fallback dialect for unsupported databases
/// Implements only core SQL-92 features with maximum compatibility
/// </summary>
public class Sql92Dialect : SqlDialect
{
    public Sql92Dialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
    public override string ParameterMarker => "?";  // SQL-92 standard positional parameters
    public override bool SupportsNamedParameters => false;  // SQL-92 only has positional
    public override int MaxParameterLimit => 255;  // Conservative limit
    public override int ParameterNameMaxLength => 18;  // SQL-92 identifier limit
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    // SQL-92 standard identifiers
    public override string QuotePrefix => "\"";
    public override string QuoteSuffix => "\"";

    // Only SQL-92 compliant features enabled
    public override bool SupportsMerge => false;
    public override bool SupportsInsertOnConflict => false;
    public override bool SupportsJsonTypes => false;
    public override bool SupportsArrayTypes => false;
    public override bool SupportsWindowFunctions => false;
    public override bool SupportsCommonTableExpressions => false;
    public override bool SupportsXmlTypes => false;
    public override bool SupportsTemporalData => false;
    public override bool SupportsEnhancedWindowFunctions => false;
    public override bool SupportsRowPatternMatching => false;
    public override bool SupportsMultidimensionalArrays => false;
    public override bool SupportsPropertyGraphQueries => false;
    public override bool SupportsUserDefinedTypes => false;
    public override bool SupportsRegularExpressions => false;
    public override bool SupportsInsteadOfTriggers => false;
    public override bool SupportsTruncateTable => false;
    public override bool SupportsNamespaces => false;  // Conservative - not all SQL-92 DBs support schemas

    // SQL-92 does support these basic features
    public override bool SupportsIntegrityConstraints => true;
    public override bool SupportsJoins => true;
    public override bool SupportsOuterJoins => true;
    public override bool SupportsSubqueries => true;
    public override bool SupportsUnion => true;

    public override string GetVersionQuery() => "SELECT 'SQL-92 Compatible Database' AS version";

    public override string GetConnectionSessionSettings()
    {
        // No session settings - maximum compatibility
        return string.Empty;
    }

    public override void ApplyConnectionSettings(IDbConnection connection)
    {
        // No settings to apply for maximum compatibility
        Logger.LogDebug("Using SQL-92 fallback dialect - no connection settings applied");
    }

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        // Always return SQL-92 as this is our baseline
        return SqlStandardLevel.Sql92;
    }

    protected override string ExtractProductNameFromVersion(string versionString)
    {
        return "Unknown Database (SQL-92 Compatible)";
    }

    public override string MakeParameterName(string parameterName)
    {
        // SQL-92 uses positional parameters only
        return "?";
    }

    public override string MakeParameterName(DbParameter dbParameter)
    {
        // SQL-92 uses positional parameters only
        return "?";
    }

    public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        var parameter = base.CreateDbParameter(name, type, value);
        
        // Clear the parameter name for positional parameters
        parameter.ParameterName = string.Empty;
        
        // Ensure compatibility with basic types only
        switch (type)
        {
            case DbType.Guid:
                // Convert GUID to string for maximum compatibility
                parameter.DbType = DbType.String;
                if (value is Guid guidValue)
                {
                    parameter.Value = guidValue.ToString();
                    parameter.Size = 36; // Standard GUID string length
                }
                break;
                
            case DbType.Boolean:
                // Convert boolean to integer for SQL-92 compatibility
                parameter.DbType = DbType.Int16;
                if (value is bool boolValue)
                {
                    parameter.Value = boolValue ? (short)1 : (short)0;
                }
                break;
                
            case DbType.DateTimeOffset:
                // Convert to basic DateTime for SQL-92 compatibility
                parameter.DbType = DbType.DateTime;
                if (value is DateTimeOffset dtoValue)
                {
                    parameter.Value = dtoValue.DateTime;
                }
                break;
        }

        return parameter;
    }

    protected override async Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        // Try some common version queries, fallback gracefully
        var versionQueries = new[]
        {
            "SELECT version()",
            "SELECT @@version",
            "SELECT * FROM v$version WHERE rownum = 1",
            GetVersionQuery()
        };

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
            catch
            {
                // Continue to next query
                continue;
            }
        }

        return "Unknown Version (SQL-92 Compatible)";
    }

    protected override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        // Try to get product name from schema metadata first
        try
        {
            var schema = connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
            if (schema.Rows.Count > 0)
            {
                var productName = schema.Rows[0].Field<string>("DataSourceProductName");
                if (!string.IsNullOrEmpty(productName))
                {
                    Logger.LogWarning("Using SQL-92 fallback dialect for detected database: {ProductName}", productName);
                    return productName;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not retrieve product name from schema metadata");
        }

        Logger.LogWarning("Using SQL-92 fallback dialect for unknown database product");
        return null;
    }
}