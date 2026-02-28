using System.Globalization;

namespace testbed.Snowflake;

public sealed class SnowflakeTestConfiguration
{
    private const string DefaultSchema = "PUBLIC";
    private const string DefaultPrefix = "PENGDOWS_TEST";

    public SnowflakeTestConfiguration(
        string account,
        string user,
        string password,
        string warehouse,
        string? role,
        string? adminDatabase,
        string testDatabase,
        string testSchema,
        bool useDatabaseIsolation,
        string adminConnectionString,
        string testConnectionString)
    {
        Account = account;
        User = user;
        Password = password;
        Warehouse = warehouse;
        Role = role;
        AdminDatabase = adminDatabase;
        TestDatabase = testDatabase;
        TestSchema = testSchema;
        UseDatabaseIsolation = useDatabaseIsolation;
        AdminConnectionString = adminConnectionString;
        TestConnectionString = testConnectionString;
    }

    public string Account { get; }
    public string User { get; }
    public string Password { get; }
    public string Warehouse { get; }
    public string? Role { get; }
    public string? AdminDatabase { get; }
    public string TestDatabase { get; }
    public string TestSchema { get; }
    public bool UseDatabaseIsolation { get; }
    public string AdminConnectionString { get; }
    public string TestConnectionString { get; }

    public static SnowflakeTestConfiguration FromEnvironment(
        Func<string, string?>? getEnv = null,
        Func<long>? utcNowSeconds = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        var account = getEnv("SNOWFLAKE_ACCOUNT");
        var user = getEnv("SNOWFLAKE_USER");
        var password = getEnv("SNOWFLAKE_PASSWORD");
        var warehouse = getEnv("SNOWFLAKE_WAREHOUSE");
        var role = getEnv("SNOWFLAKE_ROLE");

        var createDatabase = string.Equals(getEnv("SNOWFLAKE_CREATE_DATABASE"), "true",
            StringComparison.OrdinalIgnoreCase);
        var baseDatabase = getEnv("SNOWFLAKE_DATABASE");
        var adminDatabase = getEnv("SNOWFLAKE_ADMIN_DATABASE") ?? baseDatabase;
        var schema = getEnv("SNOWFLAKE_SCHEMA") ?? DefaultSchema;
        var prefix = getEnv("SNOWFLAKE_TEST_PREFIX") ?? DefaultPrefix;
        var timestamp = utcNowSeconds?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var uniqueSuffix = utcNowSeconds is null
            ? $"{timestamp}_{Random.Shared.Next(1000, 9999)}"
            : timestamp.ToString(CultureInfo.InvariantCulture);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(account)) missing.Add("SNOWFLAKE_ACCOUNT");
        if (string.IsNullOrWhiteSpace(user)) missing.Add("SNOWFLAKE_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("SNOWFLAKE_PASSWORD");
        if (string.IsNullOrWhiteSpace(warehouse)) missing.Add("SNOWFLAKE_WAREHOUSE");
        if (!createDatabase && string.IsNullOrWhiteSpace(baseDatabase)) missing.Add("SNOWFLAKE_DATABASE");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"[Snowflake] Missing required environment variables: {string.Join(", ", missing)}. " +
                "Set INCLUDE_SNOWFLAKE=true and provide all required SNOWFLAKE_* variables to enable Snowflake testing.");
        }

        var testDatabase = createDatabase ? $"{prefix}_{uniqueSuffix}" : baseDatabase!;
        var testSchema = createDatabase ? schema : $"{prefix}_{uniqueSuffix}";

        var adminSchema = string.IsNullOrWhiteSpace(adminDatabase) ? null : DefaultSchema;
        var adminConnectionString =
            BuildConnectionString(account!, user!, password!, warehouse!, role, adminDatabase, adminSchema);
        var testConnectionString =
            BuildConnectionString(account!, user!, password!, warehouse!, role, testDatabase, testSchema);

        return new SnowflakeTestConfiguration(
            account!,
            user!,
            password!,
            warehouse!,
            role,
            adminDatabase,
            testDatabase,
            testSchema,
            createDatabase,
            adminConnectionString,
            testConnectionString);
    }

    public IEnumerable<string> GetCreateCommands()
    {
        if (UseDatabaseIsolation)
        {
            yield return $"CREATE DATABASE IF NOT EXISTS {QuoteIdentifier(TestDatabase)}";
            yield return
                $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(TestDatabase)}.{QuoteIdentifier(TestSchema)}";
        }
        else
        {
            yield return $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(TestSchema)}";
        }
    }

    public IEnumerable<string> GetDropCommands()
    {
        if (UseDatabaseIsolation)
        {
            yield return $"DROP DATABASE IF EXISTS {QuoteIdentifier(TestDatabase)}";
        }
        else
        {
            yield return $"DROP SCHEMA IF EXISTS {QuoteIdentifier(TestSchema)} CASCADE";
        }
    }

    private static string BuildConnectionString(
        string account,
        string user,
        string password,
        string warehouse,
        string? role,
        string? database,
        string? schema)
    {
        var parts = new List<string>
        {
            $"account={account}",
            $"user={user}",
            $"password={password}",
            $"warehouse={warehouse}"
        };

        if (!string.IsNullOrWhiteSpace(role))
        {
            parts.Add($"role={role}");
        }

        if (!string.IsNullOrWhiteSpace(database))
        {
            parts.Add($"db={database}");
        }

        if (!string.IsNullOrWhiteSpace(schema))
        {
            parts.Add($"schema={schema}");
        }

        return string.Join(";", parts);
    }

    private static string QuoteIdentifier(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
