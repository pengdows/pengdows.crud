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
    public override int MaxParameterLimit => 999;
    public override int ParameterNameMaxLength => 255;
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    public override bool SupportsInsertOnConflict => true;
    public override bool SupportsMerge => false;
    public override bool SupportsNamespaces => false;
    public override bool SupportsJsonTypes => IsInitialized && ProductInfo.ParsedVersion >= new Version(3, 45);
    public override bool SupportsWindowFunctions => IsInitialized && ProductInfo.ParsedVersion >= new Version(3, 25);
    public override bool SupportsCommonTableExpressions => IsInitialized && ProductInfo.ParsedVersion >= new Version(3, 8, 3);

    public override string GetVersionQuery() => "SELECT sqlite_version()";

    public override string GetConnectionSessionSettings()
    {
        return "PRAGMA foreign_keys = ON;";
    }

    protected override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
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

    protected override string ExtractProductNameFromVersion(string versionString)
    {
        return "SQLite";
    }

    protected override SqlStandardLevel DetermineStandardCompliance(Version? version)
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
}
