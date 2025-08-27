using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
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


    protected override string ExtractProductNameFromVersion(string versionString)
    {
        return "Unknown Database (SQL-92 Compatible)";
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

    public override DataTable GetDataSourceInformationSchema(ITrackedConnection connection)
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
}