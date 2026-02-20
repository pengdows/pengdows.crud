using pengdows.crud;
using Snowflake.Data.Client;

namespace testbed.Snowflake;

/// <summary>
/// Snowflake test container that assumes Snowflake is already running externally.
/// Credentials are supplied via environment variables; no Docker image is available.
/// Required env vars: SNOWFLAKE_ACCOUNT, SNOWFLAKE_USER, SNOWFLAKE_PASSWORD, SNOWFLAKE_WAREHOUSE, SNOWFLAKE_DATABASE.
/// Optional env vars: SNOWFLAKE_SCHEMA (default: PUBLIC).
/// </summary>
public class SnowflakeTestContainer : TestContainer
{
    private readonly string _connectionString;
    private readonly string _testSchema;

    public SnowflakeTestContainer()
    {
        var account = Environment.GetEnvironmentVariable("SNOWFLAKE_ACCOUNT");
        var user = Environment.GetEnvironmentVariable("SNOWFLAKE_USER");
        var password = Environment.GetEnvironmentVariable("SNOWFLAKE_PASSWORD");
        var warehouse = Environment.GetEnvironmentVariable("SNOWFLAKE_WAREHOUSE");
        var database = Environment.GetEnvironmentVariable("SNOWFLAKE_DATABASE");
        var schema = Environment.GetEnvironmentVariable("SNOWFLAKE_SCHEMA") ?? "PUBLIC";

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(account)) missing.Add("SNOWFLAKE_ACCOUNT");
        if (string.IsNullOrWhiteSpace(user)) missing.Add("SNOWFLAKE_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("SNOWFLAKE_PASSWORD");
        if (string.IsNullOrWhiteSpace(warehouse)) missing.Add("SNOWFLAKE_WAREHOUSE");
        if (string.IsNullOrWhiteSpace(database)) missing.Add("SNOWFLAKE_DATABASE");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"[Snowflake] Missing required environment variables: {string.Join(", ", missing)}. " +
                "Set INCLUDE_SNOWFLAKE=true and provide all SNOWFLAKE_* variables to enable Snowflake testing.");
        }

        // Each test run gets an isolated schema to avoid cross-run interference
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _testSchema = $"PENGDOWS_TEST_{timestamp}";

        _connectionString =
            $"account={account};user={user};password={password};warehouse={warehouse};" +
            $"db={database};schema={_testSchema}";
    }

    public override async Task StartAsync()
    {
        try
        {
            // Use the base schema (PUBLIC) to create the test schema first
            var account = Environment.GetEnvironmentVariable("SNOWFLAKE_ACCOUNT");
            var user = Environment.GetEnvironmentVariable("SNOWFLAKE_USER");
            var password = Environment.GetEnvironmentVariable("SNOWFLAKE_PASSWORD");
            var warehouse = Environment.GetEnvironmentVariable("SNOWFLAKE_WAREHOUSE");
            var database = Environment.GetEnvironmentVariable("SNOWFLAKE_DATABASE");

            var adminConnString =
                $"account={account};user={user};password={password};warehouse={warehouse};" +
                $"db={database};schema=PUBLIC";

            await using var conn = SnowflakeDbFactory.Instance.CreateConnection();
            conn.ConnectionString = adminConnString;
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {_testSchema}";
            await cmd.ExecuteNonQueryAsync();

            await conn.CloseAsync();

            Console.WriteLine($"[Snowflake] Created test schema {_testSchema}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[Snowflake] Cannot connect to Snowflake or create test schema. " +
                $"Error: {ex.Message}", ex);
        }
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        var context = new DatabaseContext(_connectionString, SnowflakeDbFactory.Instance);
        return Task.FromResult<IDatabaseContext>(context);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            var account = Environment.GetEnvironmentVariable("SNOWFLAKE_ACCOUNT");
            var user = Environment.GetEnvironmentVariable("SNOWFLAKE_USER");
            var password = Environment.GetEnvironmentVariable("SNOWFLAKE_PASSWORD");
            var warehouse = Environment.GetEnvironmentVariable("SNOWFLAKE_WAREHOUSE");
            var database = Environment.GetEnvironmentVariable("SNOWFLAKE_DATABASE");

            var adminConnString =
                $"account={account};user={user};password={password};warehouse={warehouse};" +
                $"db={database};schema=PUBLIC";

            await using var conn = SnowflakeDbFactory.Instance.CreateConnection();
            conn.ConnectionString = adminConnString;
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS {_testSchema} CASCADE";
            await cmd.ExecuteNonQueryAsync();

            await conn.CloseAsync();

            Console.WriteLine($"[Snowflake] Dropped test schema {_testSchema}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Snowflake] Warning: Failed to drop test schema {_testSchema}: {ex.Message}");
        }
    }
}
