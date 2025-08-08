#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using pengdow.crud;

#endregion

namespace testbed.Cockroach;

public class CockroachDbTestContainer : TestContainer
{
    private IContainer? _container;

    public override async Task StartAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("cockroachdb/cockroach:v25.1.0")
            .WithName("test-cockroach")
            .WithHostname("cockroach")
            .WithPortBinding(26257, 26257)
            .WithPortBinding(8080, true)
            .WithCommand("start-single-node", "--insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(26257))
            .Build();

        await _container.StartAsync();
        await Task.Delay(5000); // Allow some time for startup

        // Create the test database
        var connectionString = "Host=localhost;Port=26257;Username=root;SSL Mode=disable;";
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE DATABASE IF NOT EXISTS testdb;";
        await cmd.ExecuteNonQueryAsync();
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        var cs = "Host=localhost;Port=26257;Username=root;Database=testdb;SSL Mode=disable;";
        var ctx = new DatabaseContext(cs, NpgsqlFactory.Instance, services.GetRequiredService<ITypeMapRegistry>());
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