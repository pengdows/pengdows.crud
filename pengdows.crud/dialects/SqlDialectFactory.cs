using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Factory for creating dialect instances with automatic detection
/// </summary>
public static class SqlDialectFactory
{
    public static async Task<SqlDialect> CreateDialectAsync(
        ITrackedConnection connection,
        DbProviderFactory factory,
         ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<SqlDialect>();

        var inferredType = InferDatabaseTypeFromProvider(factory);
        var dialect = CreateDialectForType(inferredType, factory, logger);

        try
        {
            var productInfo = await dialect.DetectDatabaseInfoAsync(connection);

            if (productInfo.DatabaseType != inferredType && productInfo.DatabaseType != SupportedDatabase.Unknown)
            {
                logger.LogInformation(
                    "Database type mismatch. Inferred: {Inferred}, Detected: {Detected}. Using detected type.",
                    inferredType,
                    productInfo.DatabaseType);

                dialect = CreateDialectForType(productInfo.DatabaseType, factory, logger);
                await dialect.DetectDatabaseInfoAsync(connection);
            }

            return dialect;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to detect database type, falling back to inferred type: {Type}", inferredType);
            return dialect;
        }
    }
    public static SqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory)
    {
        return CreateDialectAsync(connection, factory,  NullLoggerFactory.Instance).GetAwaiter().GetResult();
    }


    public static SqlDialect CreateDialect(
        ITrackedConnection connection,
        DbProviderFactory factory,
           ILoggerFactory loggerFactory)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        return CreateDialectAsync(connection, factory, loggerFactory).GetAwaiter().GetResult();
    }

    public static SqlDialect CreateDialectForType(
        SupportedDatabase databaseType,
        DbProviderFactory factory,
        ILogger logger)
    {
        return databaseType switch
        {
            SupportedDatabase.SqlServer => new SqlServerDialect(factory, logger),
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, logger),
            SupportedDatabase.CockroachDb => new PostgreSqlDialect(factory, logger),
            SupportedDatabase.MySql => new MySqlDialect(factory, logger),
            SupportedDatabase.MariaDb => new MySqlDialect(factory, logger),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, logger),
            SupportedDatabase.Oracle => new OracleDialect(factory, logger),
            SupportedDatabase.Firebird => throw new NotImplementedException("Firebird dialect not yet implemented"),
            _ => throw new ArgumentException($"Unsupported database type: {databaseType}")
        };
    }

    private static SupportedDatabase InferDatabaseTypeFromProvider(DbProviderFactory factory)
    {
        var typeName = factory.GetType().Name.ToLowerInvariant();

        return typeName switch
        {
            var name when name.Contains("sqlserver") || name.Contains("system.data.sqlclient") => SupportedDatabase.SqlServer,
            var name when name.Contains("npgsql") => SupportedDatabase.PostgreSql,
            var name when name.Contains("mysql") => SupportedDatabase.MySql,
            var name when name.Contains("sqlite") => SupportedDatabase.Sqlite,
            var name when name.Contains("oracle") => SupportedDatabase.Oracle,
            var name when name.Contains("firebird") => SupportedDatabase.Firebird,
            _ => SupportedDatabase.Unknown
        };
    }
}
