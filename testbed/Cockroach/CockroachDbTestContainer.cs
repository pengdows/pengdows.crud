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
    private int _sqlPort = 26257;
    private readonly string _image;

    public CockroachDbTestContainer(string image = "cockroachdb/cockroach:v25.1.0")
    {
        _image = image;
    }

    public override async Task StartAsync()
    {
        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        if (string.IsNullOrWhiteSpace(runId))
        {
            runId = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        }

        var uniqueSuffix = $"{runId}-{Guid.NewGuid():N}";

        _container = new ContainerBuilder()
            .WithImage(_image)
            .WithName($"test-cockroach-{uniqueSuffix}")
            .WithHostname("cockroach")
            .WithPortBinding(26257, true)
            .WithPortBinding(8080, true)
            .WithCommand("start-single-node", "--insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(26257))
            .Build();

        await _container.StartAsync();

        _sqlPort = _container.GetMappedPublicPort(26257);

        // Create the test database
        var connectionString = $"Host=localhost;Port={_sqlPort};Username=root;SSL Mode=disable;";
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE DATABASE IF NOT EXISTS testdb;";
        await cmd.ExecuteNonQueryAsync();
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        // Timeout=30;CommandTimeout=60 matches YugabyteTestContainer's explicit values (its
        // direct architectural sibling here -- both are distributed-consensus databases on the
        // same Npgsql wire protocol). Left unset, this container silently fell back to Npgsql's
        // single-node-oriented defaults (Timeout=15;CommandTimeout=30) -- identical to plain
        // PostgreSql's explicit values -- even though CockroachDB's distributed commit path can
        // genuinely take longer under contention than a single-node database ever would. This gap
        // was implicated in a real "Exception while reading from stream... Timeout during reading
        // attempt" failure observed under heavy parallel Docker load (12 providers' containers
        // competing for CPU) running this exact test suite.
        var cs = $"Host=localhost;Port={_sqlPort};Username=root;Database=testdb;SSL Mode=disable;" +
                 "Timeout=30;CommandTimeout=60;";
        var ctx = new DatabaseContext(cs, NpgsqlFactory.Instance);
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