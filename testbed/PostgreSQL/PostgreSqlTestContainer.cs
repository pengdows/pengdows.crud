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
    private string _database = "postgres";
    private string _password = "mysecretpassword";
    private int _port = 5432;
    private string _username = "postgres";

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

        var builder = new ContainerBuilder()
            .WithImage(image ?? "postgres:16.4")
            .WithPortBinding(_port, true);

        builder = useAdminPasswordEnvVar
            ? builder.WithEnvironment("PG_ADMIN_PASSWORD", _password)
            : builder
                .WithEnvironment("POSTGRES_PASSWORD", _password)
                .WithEnvironment("POSTGRES_USER", _username)
                .WithEnvironment("POSTGRES_DB", _database);

        _container = builder.Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(_port);
        _connectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Password={_password};Database={_database};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";
        await WaitForDbToStart(NpgsqlFactory.Instance, _connectionString, _container, 90);
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