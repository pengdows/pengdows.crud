#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;

#endregion

namespace testbed.PostgreSQL;

public class PostgreSqlTestContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    // Fixed and used only for the image's own bootstrap (POSTGRES_DB env var) and as the admin
    // connection target to run CREATE/DROP DATABASE against. Must stay fixed, not randomized:
    // WithReuse matches containers by hashing the builder config (image, env vars, ports), so if
    // this were randomized per instance, a net8.0 and net10.0 process would each compute a
    // different hash and never recognize each other's container as reusable.
    private readonly string _adminDatabase = "postgres";
    // The actual working database every DatabaseContext from this instance connects to. Random
    // per instance (not per container - a reused container answers to many instances over its
    // life) so two processes sharing one physical container, even concurrently, never see each
    // other's tables.
    private readonly string _database = "crud_test_" + TestContainerReuse.NewSuffix();
    private string _password = "mysecretpassword";
    private int _port = 5432;
    private string _username = "postgres";
    private readonly string _image;

    /// <param name="image">Image tag. Defaults to vanilla postgres:16.4.</param>
    /// <param name="port">
    /// Override for the in-container port to bind/connect to. Only needed for a fork that
    /// doesn't listen on the standard 5432 - confirmed for Fujitsu Enterprise Postgres, which
    /// defaults to 27500 (found by running the image directly and reading its startup log,
    /// not documented anywhere obvious).
    /// </param>
    /// <param name="useAdminPasswordEnvVar">
    /// True to configure via PG_ADMIN_PASSWORD (sets the password on the superuser role initdb
    /// already created) instead of POSTGRES_USER/POSTGRES_PASSWORD/POSTGRES_DB (which ask the
    /// entrypoint to CREATE ROLE a new one). Needed for Fujitsu Enterprise Postgres specifically:
    /// its entrypoint has no "skip if this is the default superuser" special-case the way the
    /// vanilla postgres image's own entrypoint does, so PG_USER=postgres crashes the container
    /// outright with "role postgres already exists" - confirmed by running the image directly.
    /// </param>
    public PostgreSqlTestContainer(string? image = null, int? port = null, bool useAdminPasswordEnvVar = false)
    {
        _port = port ?? _port;
        _image = image ?? "postgres:16.4";

        var builder = new ContainerBuilder()
            .WithImage(_image)
            .WithPortBinding(_port, true);

        builder = useAdminPasswordEnvVar
            ? builder.WithEnvironment("PG_ADMIN_PASSWORD", _password)
            : builder
                .WithEnvironment("POSTGRES_PASSWORD", _password)
                .WithEnvironment("POSTGRES_USER", _username)
                .WithEnvironment("POSTGRES_DB", _adminDatabase);

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
            using var _ = await TestContainerReuse.AcquireStartupLockAsync("postgres-" + _image);
            await _container.StartAsync();
        }
        else
        {
            await _container.StartAsync();
        }

        var hostPort = _container.GetMappedPublicPort(_port);
        var adminConnectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Password={_password};Database={_adminDatabase};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";
        await WaitForDbToStart(NpgsqlFactory.Instance, adminConnectionString, _container, 90);

        await using (var conn = new NpgsqlConnection(adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_database}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        _connectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Password={_password};Database={_database};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";
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

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionString is not null)
        {
            try
            {
                var hostPort = _container.GetMappedPublicPort(_port);
                var adminConnectionString =
                    $@"Host=localhost;Port={hostPort};Username={_username};Password={_password};Database={_adminDatabase};Pooling=false;Timeout=15;CommandTimeout=30;";
                await using var conn = new NpgsqlConnection(adminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort: a shared/reused container may already be gone, or another process
                // may be mid-shutdown. Leaking one randomly-named test database is harmless.
            }
        }

        // WithReuse disables Testcontainers' own Ryuk-based cleanup, but does nothing about this
        // explicit dispose call - without this guard, whichever process (net8.0/net10.0) finishes
        // first would stop/remove the container out from under the other.
        if (!TestContainerReuse.Enabled)
        {
            await _container.DisposeAsync();
        }
    }
}