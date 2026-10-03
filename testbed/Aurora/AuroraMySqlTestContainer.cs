using MySqlConnector;
using pengdows.crud;

namespace testbed.Aurora;

public sealed class AuroraMySqlTestContainer : TestContainer
{
    private readonly AuroraTestConfiguration _config = AuroraTestConfiguration.FromEnvironment(AuroraEngine.MySql);
    private bool _created;

    public override async Task StartAsync()
    {
        await using var conn = new MySqlConnection(_config.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS {Quote(_config.TestNamespace)}";
        await cmd.ExecuteNonQueryAsync();
        _created = true;
        Console.WriteLine($"[Aurora MySQL] Using database {_config.TestNamespace} on {_config.Host}");
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
        => Task.FromResult<IDatabaseContext>(new DatabaseContext(_config.TestConnectionString, MySqlConnectorFactory.Instance));

    protected override async ValueTask DisposeAsyncCore()
    {
        if (!_created) return;
        try
        {
            await using var conn = new MySqlConnection(_config.AdminConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS {Quote(_config.TestNamespace)}";
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"[Aurora MySQL] Dropped database {_config.TestNamespace}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Aurora MySQL] Warning: cleanup failed for {_config.TestNamespace}: {ex.Message}");
        }
    }

    private static string Quote(string value) => "`" + value.Replace("`", "``") + "`";
}
