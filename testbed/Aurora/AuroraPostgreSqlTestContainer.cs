using Npgsql;
using pengdows.crud;

namespace testbed.Aurora;

public sealed class AuroraPostgreSqlTestContainer : TestContainer
{
    private readonly AuroraTestConfiguration _config = AuroraTestConfiguration.FromEnvironment(AuroraEngine.PostgreSql);
    private bool _created;

    public override async Task StartAsync()
    {
        await using var conn = new NpgsqlConnection(_config.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {Quote(_config.TestNamespace)}";
        await cmd.ExecuteNonQueryAsync();
        _created = true;
        Console.WriteLine($"[Aurora PostgreSQL] Using schema {_config.TestNamespace} on {_config.Host}");
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
        => Task.FromResult<IDatabaseContext>(new DatabaseContext(_config.TestConnectionString, NpgsqlFactory.Instance));

    protected override async ValueTask DisposeAsyncCore()
    {
        if (!_created) return;
        try
        {
            await using var conn = new NpgsqlConnection(_config.AdminConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS {Quote(_config.TestNamespace)} CASCADE";
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"[Aurora PostgreSQL] Dropped schema {_config.TestNamespace}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Aurora PostgreSQL] Warning: cleanup failed for {_config.TestNamespace}: {ex.Message}");
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
