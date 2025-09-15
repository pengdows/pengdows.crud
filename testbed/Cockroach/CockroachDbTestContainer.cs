#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;

#endregion

namespace testbed.Cockroach;

public class CockroachDbTestContainer : TestContainer
{
    private IContainer? _container;
    private int _mappedSqlPort;

    public override async Task StartAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("cockroachdb/cockroach:v25.1.0")
            .WithName("test-cockroach")
            .WithHostname("cockroach")
            // bind SQL port to a random host port to avoid conflicts
            .WithPortBinding(26257, true)
            .WithPortBinding(8080, true)
            .WithCommand("start-single-node", "--insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(26257))
            .Build();

        await _container.StartAsync();

        // Create the test database using the mapped host port
        _mappedSqlPort = _container.GetMappedPublicPort(26257);
        var connectionString = $"Host=localhost;Port={_mappedSqlPort};Username=root;SSL Mode=Disable;Trust Server Certificate=true;Timeout=5";

        // Cockroach may report port availability before SQL service is fully ready; retry a few times.
        const int maxAttempts = 10;
        var attempt = 0;
        Exception? last = null;
        while (attempt++ < maxAttempts)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE DATABASE IF NOT EXISTS testdb;";
                await cmd.ExecuteNonQueryAsync();
                last = null;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(1000);
            }
        }
        if (last != null)
        {
            throw new InvalidOperationException($"Failed to initialize CockroachDB: {last.Message}", last);
        }
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_mappedSqlPort == 0 && _container != null)
        {
            _mappedSqlPort = _container.GetMappedPublicPort(26257);
        }
        var cs = $"Host=localhost;Port={_mappedSqlPort};Username=root;Database=testdb;SSL Mode=Disable;Trust Server Certificate=true;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=20;Timeout=15;CommandTimeout=30;";
        var ctx = new DatabaseContext(cs, NpgsqlFactory.Instance, null!);
        return Task.FromResult<IDatabaseContext>(ctx);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }
}
