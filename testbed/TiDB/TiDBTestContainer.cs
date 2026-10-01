#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySql.Data.MySqlClient;
using pengdows.crud;

#endregion

namespace testbed.TiDB;

public class TiDBTestContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    // Fixed - TiDB's own built-in default, used as the admin connection target to run
    // CREATE/DROP DATABASE against.
    private const string _adminDatabase = "test";
    // Random per instance so two processes sharing one physical container never see each other's
    // tables, even if they run concurrently.
    private readonly string _database = "crud_test_" + TestContainerReuse.NewSuffix();
    private const int _port = 4000;
    private const string _username = "root";
    private readonly string _image;

    public TiDBTestContainer(string? image = null)
    {
        _image = image ?? "pingcap/tidb:v8.5.7";
        var builder = new ContainerBuilder()
            .WithImage(_image)
            .WithPortBinding(_port, true)
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
            using var _ = await TestContainerReuse.AcquireStartupLockAsync("tidb-" + _image);
            await _container.StartAsync();
        }
        else
        {
            await _container.StartAsync();
        }

        var hostPort = _container.GetMappedPublicPort(_port);
        // TiDB default has no password for root
        var adminConnectionString =
            $@"Server=localhost;Port={hostPort};User={_username};Database={_adminDatabase};Pooling=true;MinimumPoolSize=1;MaximumPoolSize=100;ConnectionTimeout=15;";
        await WaitForDbToStart(MySqlClientFactory.Instance, adminConnectionString, _container);

        await using (var conn = new MySql.Data.MySqlClient.MySqlConnection(adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE `{_database}`";
            await cmd.ExecuteNonQueryAsync();
        }

        _connectionString =
            $@"Server=localhost;Port={hostPort};User={_username};Database={_database};Pooling=true;MinimumPoolSize=1;MaximumPoolSize=100;ConnectionTimeout=15;";
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, MySqlClientFactory.Instance, new TypeMapRegistry()));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionString is not null)
        {
            try
            {
                var hostPort = _container.GetMappedPublicPort(_port);
                var adminConnectionString =
                    $@"Server=localhost;Port={hostPort};User={_username};Database={_adminDatabase};ConnectionTimeout=15;";
                await using var conn = new MySql.Data.MySqlClient.MySqlConnection(adminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
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