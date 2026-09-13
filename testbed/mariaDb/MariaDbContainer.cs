#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySqlConnector;
using pengdows.crud;

#endregion

namespace testbed.mariaDb;

public class MariaDbContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    // Fixed - used only for the image's own bootstrap. Must stay fixed so WithReuse's config
    // hash matches across processes (see TestContainerReuse).
    private readonly string _bootstrapDatabase = "testdb";
    // Random per instance so two processes sharing one physical container never see each other's
    // tables, even if they run concurrently.
    private readonly string _database = "crud_test_" + TestContainerReuse.NewSuffix();
    private string _password = "rootpassword";
    private int _port = 3306;
    private string _username = "root";
    private readonly string _image;

    public MariaDbContainer(string? image = null)
    {
        _image = image ?? "mariadb:11.4.12";
        var builder = new ContainerBuilder()
            .WithImage(_image)
            .WithEnvironment("MARIADB_ROOT_PASSWORD", _password)
            .WithEnvironment("MYSQL_ROOT_PASSWORD", _password)
            .WithEnvironment("MARIADB_DATABASE", _bootstrapDatabase)
            .WithEnvironment("MYSQL_DATABASE", _bootstrapDatabase)
            .WithEnvironment("MYSQL_SQL_MODE",
                "STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES")
            .WithCommand("--character-set-server=utf8mb4", "--collation-server=utf8mb4_unicode_ci")
            .WithPortBinding(_port, true)
            .WithExposedPort(_port);

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
            using var _ = await TestContainerReuse.AcquireStartupLockAsync("mariadb-" + _image);
            await _container.StartAsync();
        }
        else
        {
            await _container.StartAsync();
        }

        var hostPort = _container.GetMappedPublicPort(_port);
        var adminConnectionString =
            $@"Server=localhost;Port={hostPort};Database={_bootstrapDatabase};User ID={_username};Password={_password};";
        await WaitForDbToStart(MySqlConnectorFactory.Instance, adminConnectionString, _container);

        await using (var conn = new MySqlConnection(adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE `{_database}`";
            await cmd.ExecuteNonQueryAsync();
        }

        _connectionString =
            $@"Server=localhost;Port={hostPort};Database={_database};User ID={_username};Password={_password};";
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, MySqlConnectorFactory.Instance, new TypeMapRegistry()));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionString is not null)
        {
            try
            {
                var hostPort = _container.GetMappedPublicPort(_port);
                var adminConnectionString =
                    $@"Server=localhost;Port={hostPort};Database={_bootstrapDatabase};User ID={_username};Password={_password};";
                await using var conn = new MySqlConnection(adminConnectionString);
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