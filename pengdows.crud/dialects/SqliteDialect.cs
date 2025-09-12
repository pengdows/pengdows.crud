using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// SQLite dialect with good standard compliance despite being embedded
/// </summary>
public class SqliteDialect : SqlDialect
{
    public SqliteDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.Sqlite;
    public override string ParameterMarker => "@";
    public override bool SupportsNamedParameters => true;
    // IMMUTABLE: SQLite SQLITE_MAX_VARIABLE_NUMBER default - do not change without extensive testing
    public override int MaxParameterLimit => 999;
    // IMMUTABLE: SQLite identifier length limit - do not change without extensive testing
    public override int ParameterNameMaxLength => 255;
    
    // SQLite benefits from prepared statements with inherent prepare support
    public override bool PrepareStatements => true;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => false;
    public override bool SupportsSavepoints => true;
    public override bool SupportsJsonTypes => IsVersionAtLeast(3, 45);
    public override bool SupportsWindowFunctions => IsVersionAtLeast(3, 25);
    public override bool SupportsCommonTableExpressions => IsVersionAtLeast(3, 8, 3);

    public override string GetVersionQuery() => "SELECT sqlite_version()";

    public override string GetBaseSessionSettings()
    {
        return "PRAGMA foreign_keys = ON;";
    }

    public override string GetReadOnlySessionSettings()
    {
        return "PRAGMA query_only = ON;";
    }

    public override string? GetReadOnlyConnectionParameter()
    {
        return "Mode=ReadOnly";
    }

    [Obsolete]
    public override string GetConnectionSessionSettings()
    {
        return "PRAGMA foreign_keys = ON;";
    }

    public override void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly)
    {
        // SQLite: Only apply read-only connection parameter if not a memory database
        if (readOnly && IsMemoryDatabase(context.ConnectionString))
        {
            // For memory databases, just set the connection string without read-only parameter
            connection.ConnectionString = context.ConnectionString;
        }
        else
        {
            // Use base class implementation for non-memory databases
            base.ApplyConnectionSettings(connection, context, readOnly);
        }
    }

    protected override bool IsMemoryDatabase(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var lower = connectionString.ToLowerInvariant();
        return lower.Contains(":memory:") || lower.Contains("mode=memory");
    }

    public override DataTable GetDataSourceInformationSchema(ITrackedConnection connection)
    {
        var resourceName = $"pengdows.crud.xml.{SupportedDatabase.Sqlite}.schema.xml";
        using var stream = typeof(SqliteDialect).Assembly.GetManifestResourceStream(resourceName)
                        ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");
        var table = new DataTable();
        table.ReadXml(stream);
        return table;
    }

    public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT sqlite_version()";
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

            if (await reader.ReadAsync())
            {
                return "SQLite";
            }
        }
        catch
        {
        }

        return null;
    }

    public override string ExtractProductNameFromVersion(string versionString)
    {
        return "SQLite";
    }

    public override SqlStandardLevel DetermineStandardCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql92;
        }

        if (version.Major == 3)
        {
            return version.Minor switch
            {
                >= 45 => SqlStandardLevel.Sql2016,
                >= 35 => SqlStandardLevel.Sql2011,
                >= 25 => SqlStandardLevel.Sql2008,
                >= 8 => SqlStandardLevel.Sql2003,
                _ => SqlStandardLevel.Sql92
            };
        }

        return SqlStandardLevel.Sql92;
    }

    public override bool IsUniqueViolation(DbException ex)
    {
        return ex is DbException dbEx && dbEx.ErrorCode == 19;
    }
}
