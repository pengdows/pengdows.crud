#region

using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public class DataSourceInformation : IDataSourceInformation
{
    private static readonly Regex DefaultNameRegex = new("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    private static readonly Dictionary<SupportedDatabase, string> VersionQueries = new()
    {
        { SupportedDatabase.SqlServer, "SELECT @@VERSION" },
        { SupportedDatabase.MySql, "SELECT VERSION()" },
        { SupportedDatabase.MariaDb, "SELECT VERSION()" },
        { SupportedDatabase.PostgreSql, "SELECT version()" },
        { SupportedDatabase.CockroachDb, "SELECT version()" },
        { SupportedDatabase.Oracle, "SELECT * FROM v$version" },
        { SupportedDatabase.Sqlite, "SELECT sqlite_version()" },
        { SupportedDatabase.Firebird, @"SELECT rdb$get_context('SYSTEM','ENGINE_VERSION') FROM rdb$database" }
    };

    private static readonly Dictionary<SupportedDatabase, int> MaxParameterLimits = new()
    {
        { SupportedDatabase.SqlServer, 2100 },
        { SupportedDatabase.Sqlite, 999 },
        { SupportedDatabase.MySql, 65535 },
        { SupportedDatabase.MariaDb, 65535 },
        { SupportedDatabase.PostgreSql, 65535 },
        { SupportedDatabase.CockroachDb, 65535 },
        { SupportedDatabase.Oracle, 1000 },
        { SupportedDatabase.Firebird, 1499 }
    };

    private static readonly Dictionary<SupportedDatabase, ProcWrappingStyle> ProcWrapStyles = new()
    {
        { SupportedDatabase.SqlServer, ProcWrappingStyle.Exec },
        { SupportedDatabase.Oracle, ProcWrappingStyle.Oracle },
        { SupportedDatabase.MySql, ProcWrappingStyle.Call },
        { SupportedDatabase.MariaDb, ProcWrappingStyle.Call },
        { SupportedDatabase.PostgreSql, ProcWrappingStyle.PostgreSQL },
        { SupportedDatabase.CockroachDb, ProcWrappingStyle.PostgreSQL },
        { SupportedDatabase.Firebird, ProcWrappingStyle.ExecuteProcedure }
    };

    private static readonly Dictionary<SupportedDatabase, string> DefaultMarkers = new()
    {
        { SupportedDatabase.Firebird, "@" },
        { SupportedDatabase.SqlServer, "@" },
        { SupportedDatabase.MySql, "@" },
        { SupportedDatabase.MariaDb, "@" },
        { SupportedDatabase.Sqlite, "@" },
        { SupportedDatabase.PostgreSql, ":" },
        { SupportedDatabase.CockroachDb, ":" },
        { SupportedDatabase.Oracle, ":" }
    };

    private readonly ILogger<DataSourceInformation> _logger;

    private DataSourceInformation(ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<DataSourceInformation>();
        ParameterNamePatternRegex = DefaultNameRegex;
        QuotePrefix = "\"";
        QuoteSuffix = "\"";
    }

    public DataSourceInformation(pengdows.crud.dialects.SqlDialect dialect)
        : this(NullLoggerFactory.Instance)
    {
        var info = dialect.ProductInfo;
        DatabaseProductName = info.ProductName;
        DatabaseProductVersion = info.ProductVersion;
        ParsedVersion = info.ParsedVersion;
        Product = info.DatabaseType;
        StandardCompliance = info.StandardCompliance;
        ParameterMarker = dialect.ParameterMarker;
        SupportsNamedParameters = dialect.SupportsNamedParameters;
        ParameterNameMaxLength = dialect.ParameterNameMaxLength;
        ProcWrappingStyle = dialect.ProcWrappingStyle;
        MaxParameterLimit = dialect.MaxParameterLimit;
        CompositeIdentifierSeparator = dialect.CompositeIdentifierSeparator;
        ParameterMarkerPattern = string.Empty;
    }

    public DataSourceInformation(DbConnection conn, ILoggerFactory? loggerFactory = null)
        : this(loggerFactory)
    {
        if (conn == null) throw new ArgumentNullException(nameof(conn));

        var tracked = (conn as ITrackedConnection) ?? new TrackedConnection(conn);
        if (tracked.State != ConnectionState.Open) tracked.Open();

        InitializeSync(tracked);
    }

    public string DatabaseProductName { get; private set; }
    public string DatabaseProductVersion { get; private set; }
    public Version? ParsedVersion { get; private set; }
    public SupportedDatabase Product { get; private set; }
    public string ParameterMarkerPattern { get; private set; }
    public bool SupportsNamedParameters { get; private set; }
    public string ParameterMarker { get; private set; }
    public int ParameterNameMaxLength { get; private set; }
    public Regex ParameterNamePatternRegex { get; private set; }
    public string QuotePrefix { get; private set; }
    public string QuoteSuffix { get; private set; }
    public bool PrepareStatements { get; private set; }
    public ProcWrappingStyle ProcWrappingStyle { get; set; }
    public int MaxParameterLimit { get; private set; }
    public string CompositeIdentifierSeparator { get; private set; } = ".";
    public SqlStandardLevel StandardCompliance { get; private set; } = SqlStandardLevel.Sql92;

    public string GetDatabaseVersion(ITrackedConnection connection)
    {
        try
        {
            var versionQuery = Product switch
            {
                SupportedDatabase.SqlServer => "SELECT @@VERSION",
                SupportedDatabase.MySql => "SELECT VERSION()",
                SupportedDatabase.MariaDb => "SELECT VERSION()",
                SupportedDatabase.PostgreSql => "SELECT version()",
                SupportedDatabase.CockroachDb => "SELECT version()",
                SupportedDatabase.Oracle => @"SELECT * FROM v$version",
                SupportedDatabase.Sqlite => "SELECT sqlite_version()",
                SupportedDatabase.Firebird => "SELECT rdb$get_context('SYSTEM','ENGINE_VERSION') FROM rdb$database",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(versionQuery)) return "Unknown Database Version";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = versionQuery;
            var version = cmd.ExecuteScalar()?.ToString();
            return version ?? "Unknown Version";
        }
        catch (Exception ex)
        {
            return "Error retrieving version: " + ex.Message;
        }
    }

    public DataTable GetSchema(ITrackedConnection connection)
    {
        if (IsSqliteSync(connection)) return ReadSqliteSchema();

        return connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
    }

    public bool SupportsMerge => Product switch
    {
        SupportedDatabase.SqlServer => true,
        SupportedDatabase.Oracle => true,
        SupportedDatabase.Firebird => true,
        SupportedDatabase.PostgreSql => GetMajorVersion() > 14,
        _ => false
    };

    public bool SupportsInsertOnConflict => Product switch
    {
        SupportedDatabase.PostgreSql => true,
        SupportedDatabase.CockroachDb => true,
        SupportedDatabase.Sqlite => true,
        SupportedDatabase.MySql => true,
        SupportedDatabase.MariaDb => true,
        _ => false
    };

    public bool RequiresStoredProcParameterNameMatch => Product switch
    {
        SupportedDatabase.Oracle => true,
        SupportedDatabase.PostgreSql => true,
        SupportedDatabase.CockroachDb => true,
        _ => false
    };

    public static DataSourceInformation Create(ITrackedConnection connection, ILoggerFactory? loggerFactory = null)
    {
        return CreateAsync(connection, loggerFactory).GetAwaiter().GetResult();
    }

    public static Task<DataSourceInformation> CreateAsync(ITrackedConnection connection, ILoggerFactory? loggerFactory = null)
    {
        return CreateInternalAsync(connection, loggerFactory);
    }

    private static async Task<DataSourceInformation> CreateInternalAsync(ITrackedConnection connection, ILoggerFactory? loggerFactory)
    {
        var info = new DataSourceInformation(loggerFactory);
        await info.InitializeInternalAsync(connection).ConfigureAwait(false);
        return info;
    }

    private void InitializeSync(ITrackedConnection connection)
    {
        InitializeInternalAsync(connection).GetAwaiter().GetResult();
    }

    private async Task InitializeInternalAsync(ITrackedConnection connection)
    {
        var schema = await GetSchemaAsync(connection, _logger).ConfigureAwait(false);
        var rawName = schema.Rows[0].Field<string>("DataSourceProductName") ?? string.Empty;

        var initial = InferDatabaseProduct(rawName);
        VersionQueries.TryGetValue(initial, out var versionSql);

        var version = string.IsNullOrEmpty(versionSql)
            ? "Unknown Version"
            : await GetVersionAsync(connection, versionSql, _logger).ConfigureAwait(false);

        ParsedVersion = ParseVersion(version);
        DatabaseProductVersion = version;
        DatabaseProductName = rawName;

        var final = InferDatabaseProduct(version);
        if (connection.ConnectionString.ToLower().Contains("emulatedproduct="))
        {
            var csb = new DbConnectionStringBuilder
            {
                ConnectionString = connection.ConnectionString
            };
            var x = csb["EmulatedProduct"] as string;
            final = Enum.Parse<SupportedDatabase>(x, true);
        }

        Product = final != SupportedDatabase.Unknown ? final : initial;
        StandardCompliance = DetermineStandardCompliance(Product, ParsedVersion);
        schema.TableName = ((schema.TableName?.Length < 1) ? Product.ToString() : schema.TableName);
        schema.WriteXml($"{Product}.schema.xml", XmlWriteMode.WriteSchema);

        ApplySchema(schema);
        PrepareStatements = false;
    }

    private static async Task<string> GetVersionAsync(
        ITrackedConnection connection,
        string versionSql,
        ILogger<DataSourceInformation> logger)
    {
        var result = await ExecuteScalarViaReaderAsync(connection, versionSql, logger).ConfigureAwait(false);
        return result?.ToString() ?? "Unknown Version";
    }

    private void ApplySchema(DataTable schema)
    {
        var row = schema.Rows[0];

        ParameterMarkerPattern = GetField<string>(row, "ParameterMarkerPattern", string.Empty);
        ParameterNameMaxLength = GetField<int>(row, "ParameterNameMaxLength", 0);
        SupportsNamedParameters = GetField<bool>(row, "SupportsNamedParameters", false);
        var markerFormat = GetField<string>(row, "ParameterMarkerFormat", string.Empty);
        if (!string.IsNullOrWhiteSpace(markerFormat))
        {
            ParameterMarker = markerFormat.Replace("{0}", string.Empty);
        }
        else
        {
            ParameterMarker = DefaultMarkers.GetValueOrDefault(Product, "?");
        }

        SupportsNamedParameters |= ParameterMarker != "?";
        ProcWrappingStyle = ProcWrapStyles.GetValueOrDefault(Product, ProcWrappingStyle.None);
        MaxParameterLimit = MaxParameterLimits.GetValueOrDefault(Product, 999);
    }

    private static DataTable ReadSqliteSchema()
    {
        var resourceName = $"pengdows.crud.xml.{SupportedDatabase.Sqlite}.schema.xml";

        using var stream = typeof(DataSourceInformation).Assembly
                               .GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");

        var table = new DataTable();
        table.ReadXml(stream);
        return table;
    }

    private static async Task<DataTable> GetSchemaAsync(
        ITrackedConnection connection,
        ILogger<DataSourceInformation> logger)
    {
        if (await IsSqliteAsync(connection, logger).ConfigureAwait(false)) return ReadSqliteSchema();

        return connection.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
    }

    private static async Task<object?> ExecuteScalarViaReaderAsync(
        ITrackedConnection connection,
        string sql,
        ILogger<DataSourceInformation> logger)
    {
        try
        {
            await using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd
                .ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SingleResult)
                .ConfigureAwait(false);

            if (await reader.ReadAsync().ConfigureAwait(false)) return reader.GetValue(0);

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }

    private static async Task<bool> IsSqliteAsync(
        ITrackedConnection connection,
        ILogger<DataSourceInformation> logger)
    {
        try
        {
            var result = await ExecuteScalarViaReaderAsync(connection, "SELECT sqlite_version()", logger)
                .ConfigureAwait(false);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSqliteSync(ITrackedConnection connection)
    {
        try
        {
            using var cmd = (DbCommand)connection.CreateCommand();
            cmd.CommandText = "SELECT sqlite_version()";
            using var reader = cmd.ExecuteReader(CommandBehavior.SingleRow);

            return reader.Read();
        }
        catch
        {
            return false;
        }
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

    private static SupportedDatabase InferDatabaseProduct(string name)
    {
        var lower = name?.ToLowerInvariant() ?? string.Empty;

        if (lower.Contains("sql server")) return SupportedDatabase.SqlServer;
        if (lower.Contains("mariadb")) return SupportedDatabase.MariaDb;
        if (lower.Contains("mysql")) return SupportedDatabase.MySql;
        if (lower.Contains("cockroach")) return SupportedDatabase.CockroachDb;
        if (lower.Contains("npgsql")) return SupportedDatabase.PostgreSql;
        if (lower.Contains("postgres")) return SupportedDatabase.PostgreSql;
        if (lower.Contains("oracle")) return SupportedDatabase.Oracle;
        if (lower.Contains("sqlite")) return SupportedDatabase.Sqlite;
        if (lower.Contains("firebird")) return SupportedDatabase.Firebird;

        return SupportedDatabase.Unknown;
    }

    private T GetField<T>(DataRow row, string columnName, T defaultValue)
    {
        try
        {
            return row.Field<T>(columnName);
        }
        catch
        {
            return defaultValue;
        }
    }

    public int? GetMajorVersion()
    {
        if (ParsedVersion != null)
        {
            return ParsedVersion.Major;
        }

        var version = ParseVersion(DatabaseProductVersion);
        ParsedVersion = version;
        return version?.Major;
    }

    public int? GetPostgreSqlMajorVersion()
    {
        if (Product != SupportedDatabase.PostgreSql) return null;

        return GetMajorVersion();
    }

    private static Version? ParseVersion(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = Regex.Match(input, "(?<ver>\\d+(?:\\.\\d+)*)");
        if (match.Success && Version.TryParse(match.Groups["ver"].Value, out var v))
        {
            return v;
        }

        return null;
    }

    private static SqlStandardLevel DetermineStandardCompliance(SupportedDatabase product, Version? version)
    {
        return product switch
        {
            SupportedDatabase.SqlServer => DetermineSqlServerCompliance(version),
            _ => SqlStandardLevel.Sql92
        };
    }

    private static SqlStandardLevel DetermineSqlServerCompliance(Version? version)
    {
        if (version == null)
        {
            return SqlStandardLevel.Sql2008;
        }

        return version.Major switch
        {
            >= 16 => SqlStandardLevel.Sql2016,
            >= 15 => SqlStandardLevel.Sql2016,
            >= 14 => SqlStandardLevel.Sql2016,
            >= 13 => SqlStandardLevel.Sql2016,
            >= 12 => SqlStandardLevel.Sql2011,
            >= 11 => SqlStandardLevel.Sql2008,
            >= 10 => SqlStandardLevel.Sql2008,
            _ => SqlStandardLevel.Sql2003
        };
    }
}