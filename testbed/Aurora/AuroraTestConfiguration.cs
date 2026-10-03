using System.Globalization;
using MySqlConnector;
using Npgsql;

namespace testbed.Aurora;

public enum AuroraEngine
{
    PostgreSql,
    MySql
}

public sealed class AuroraTestConfiguration
{
    private readonly AuroraEngine _engine;

    private AuroraTestConfiguration(AuroraEngine engine, string host, string testNamespace,
        string adminConnectionString, string testConnectionString)
    {
        _engine = engine;
        Host = host;
        TestNamespace = testNamespace;
        AdminConnectionString = adminConnectionString;
        TestConnectionString = testConnectionString;
    }

    public string Host { get; }
    public string TestNamespace { get; }
    public string AdminConnectionString { get; }
    public string TestConnectionString { get; }

    public static AuroraTestConfiguration FromEnvironment(AuroraEngine engine,
        Func<string, string?>? getEnv = null, Func<long>? utcNowMilliseconds = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        var prefix = engine == AuroraEngine.PostgreSql ? "AURORA_POSTGRES_" : "AURORA_MYSQL_";
        var host = getEnv(prefix + "HOST");
        var portText = getEnv(prefix + "PORT");
        var user = getEnv(prefix + "USER");
        var password = getEnv(prefix + "PASSWORD");
        var database = getEnv(prefix + "DATABASE");
        var sslMode = getEnv(prefix + "SSL_MODE");
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(host)) missing.Add(prefix + "HOST");
        if (string.IsNullOrWhiteSpace(user)) missing.Add(prefix + "USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add(prefix + "PASSWORD");
        if (string.IsNullOrWhiteSpace(database)) missing.Add(prefix + "DATABASE");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"[Aurora] Missing required environment variables: {string.Join(", ", missing)}. " +
                "Set INCLUDE_AURORA=true and provide the engine-specific AURORA_* variables.");
        }

        var port = string.IsNullOrWhiteSpace(portText)
            ? engine == AuroraEngine.PostgreSql ? 5432 : 3306
            : int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and <= 65535
                ? parsed
                : throw new InvalidOperationException($"[Aurora] Invalid {prefix}PORT '{portText}'.");
        var suffix = (utcNowMilliseconds?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .ToString(CultureInfo.InvariantCulture);
        var name = engine == AuroraEngine.PostgreSql ? "aurora_pg_test_" : "aurora_mysql_test_";
        var testNamespace = name + suffix;

        if (engine == AuroraEngine.PostgreSql)
        {
            var admin = new NpgsqlConnectionStringBuilder
            {
                Host = host, Port = port, Username = user, Password = password, Database = database,
                SslMode = ParsePostgresSslMode(sslMode)
            };
            var test = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { SearchPath = testNamespace };
            return new AuroraTestConfiguration(engine, host!, testNamespace, admin.ConnectionString, test.ConnectionString);
        }

        var mysqlAdmin = new MySqlConnectionStringBuilder
        {
            Server = host, Port = (uint)port, UserID = user, Password = password, Database = database,
            SslMode = ParseMySqlSslMode(sslMode)
        };
        var mysqlTest = new MySqlConnectionStringBuilder(mysqlAdmin.ConnectionString) { Database = testNamespace };
        return new AuroraTestConfiguration(engine, host!, testNamespace, mysqlAdmin.ConnectionString, mysqlTest.ConnectionString);
    }

    private static SslMode ParsePostgresSslMode(string? value)
        => Enum.TryParse<SslMode>(value, true, out var parsed) ? parsed : SslMode.Require;

    private static MySqlSslMode ParseMySqlSslMode(string? value)
        => Enum.TryParse<MySqlSslMode>(value, true, out var parsed) ? parsed : MySqlSslMode.Required;
}
