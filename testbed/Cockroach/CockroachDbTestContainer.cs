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
    // Random per instance so two processes sharing one physical container never see each other's
    // tables, even if they run concurrently.
    private readonly string _database = "crud_test_" + TestContainerReuse.NewSuffix();

    public CockroachDbTestContainer(string image = "cockroachdb/cockroach:v25.1.0")
    {
        _image = image;
    }

    public override async Task StartAsync()
    {
        var builder = new ContainerBuilder()
            .WithImage(_image)
            .WithHostname("cockroach")
            .WithPortBinding(26257, true)
            .WithPortBinding(8080, true)
            .WithCommand("start-single-node", "--insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(26257));

        if (TestContainerReuse.Enabled)
        {
            // No WithName here (a random per-instance suffix would break WithReuse's config
            // hash-matching across processes) - Testcontainers assigns its own unique name.
            builder = builder.WithReuse(true);
        }
        else
        {
            var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
            if (string.IsNullOrWhiteSpace(runId))
            {
                runId = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            }

            var uniqueSuffix = $"{runId}-{Guid.NewGuid():N}";
            builder = builder.WithName($"test-cockroach-{uniqueSuffix}");
        }

        _container = builder.Build();

        if (TestContainerReuse.Enabled)
        {
            using var _ = await TestContainerReuse.AcquireStartupLockAsync("cockroach-" + _image);
            await _container.StartAsync();
        }
        else
        {
            await _container.StartAsync();
        }

        _sqlPort = _container.GetMappedPublicPort(26257);

        // Create the test database
        var connectionString = $"Host=localhost;Port={_sqlPort};Username=root;SSL Mode=disable;";
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS \"{_database}\";";
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
        var cs = $"Host=localhost;Port={_sqlPort};Username=root;Database={_database};SSL Mode=disable;" +
                 "Timeout=30;CommandTimeout=60;";
        var ctx = new DatabaseContext(cs, NpgsqlFactory.Instance);
        return Task.FromResult<IDatabaseContext>(ctx);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_container is null)
        {
            return;
        }

        try
        {
            var connectionString = $"Host=localhost;Port={_sqlPort};Username=root;SSL Mode=disable;";
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" CASCADE;";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort: a shared/reused container may already be gone.
        }

        if (!TestContainerReuse.Enabled)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }
}