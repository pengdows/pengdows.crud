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
    // Fixed - Yugabyte's own bootstrap default, used as the admin connection target to run
    // CREATE/DROP DATABASE against.
    private const string _adminDatabase = "yugabyte";
    // Random per instance so two processes sharing one physical container never see each other's
    // tables, even if they run concurrently.
    private readonly string _database = "crud_test_" + TestContainerReuse.NewSuffix();
    private const int _port = 5433;
    private const string _username = "yugabyte";
    private readonly string _image;

    public YugabyteTestContainer(string? image = null)
    {
        _image = image ?? "yugabytedb/yugabyte:2025.2.5.2-b5";
        var builder = new ContainerBuilder()
            .WithImage(_image)
            .WithPortBinding(_port, true)
            .WithPortBinding(7000, true)
            .WithPortBinding(9000, true)
            .WithPortBinding(9042, true)
            .WithCommand("bin/yugabyted", "start", "--ui=false", "--daemon=false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port));

        if (TestContainerReuse.Enabled)
        {
            builder = builder.WithReuse(true);
        }

        _container = builder.Build();
    }

    public override async Task StartAsync()
    {
        if (TestContainerReuse.Enabled)
        {
            using var _ = await TestContainerReuse.AcquireStartupLockAsync("yugabyte-" + _image);
            await _container.StartAsync();
        }
        else
        {
            await _container.StartAsync();
        }

        var hostPort = _container.GetMappedPublicPort(_port);
        // Yugabyte default has no password for yugabyte user in insecure mode
        var adminConnectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Database={_adminDatabase};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=30;CommandTimeout=60;";
        await WaitForDbToStart(NpgsqlFactory.Instance, adminConnectionString, _container);

        // Poll until YSQL catalogs are fully initialized.
        // SELECT 1 becomes available very early (before catalog init), but detection probes such
        // as SELECT version() and pg_settings queries require the YSQL catalog to be ready.
        // Using SELECT version() as the readiness signal ensures that by the time DatabaseContext
        // runs product detection, all catalog-level queries will succeed.
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(120);
        var ready = false;
        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                await using var conn = new NpgsqlConnection(adminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT version()";
                var ver = await cmd.ExecuteScalarAsync() as string;
                if (!string.IsNullOrEmpty(ver))
                {
                    ready = true;
                    break;
                }
            }
            catch
            {
                // Not ready yet — wait and retry
            }

            await Task.Delay(2000);
        }

        if (!ready)
        {
            throw new TimeoutException("Yugabyte YSQL did not become ready in time.");
        }

        await using (var conn = new NpgsqlConnection(adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_database}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        _connectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Database={_database};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=30;CommandTimeout=60;";
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        var ctx = new DatabaseContext(_connectionString, NpgsqlFactory.Instance, new TypeMapRegistry());
        return Task.FromResult<IDatabaseContext>(ctx);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionString is not null)
        {
            try
            {
                var hostPort = _container.GetMappedPublicPort(_port);
                var adminConnectionString =
                    $@"Host=localhost;Port={hostPort};Username={_username};Database={_adminDatabase};Timeout=30;CommandTimeout=60;";
                await using var conn = new NpgsqlConnection(adminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\"";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort: a shared/reused container may already be gone.
            }
        }

        if (!TestContainerReuse.Enabled)
        {
            await _container.DisposeAsync();
        }
    }
}
