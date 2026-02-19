#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;

#endregion

namespace testbed.Yugabyte;

public class YugabyteTestContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    private const string _database = "yugabyte";
    private const int _port = 5433;
    private const string _username = "yugabyte";

    public YugabyteTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("yugabytedb/yugabyte:latest")
            .WithPortBinding(_port, true)
            .WithPortBinding(7000, true)
            .WithPortBinding(9000, true)
            .WithPortBinding(9042, true)
            .WithCommand("bin/yugabyted", "start", "--ui=false", "--daemon=false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(_port);
        // Yugabyte default has no password for yugabyte user in insecure mode
        _connectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Database={_database};Pooling=false;Timeout=30;CommandTimeout=60;";
        await WaitForDbToStart(NpgsqlFactory.Instance, _connectionString, _container);

        // Poll until YSQL catalogs are fully initialized
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync();
                return;
            }
            catch
            {
                await Task.Delay(2000);
            }
        }
        
        throw new TimeoutException("Yugabyte YSQL did not become ready in time.");
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, NpgsqlFactory.Instance, new TypeMapRegistry()));
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
