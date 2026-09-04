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

    public PostgreSqlTestContainer(string? image = null)
    {
        _container = new ContainerBuilder()
            .WithImage(image ?? "postgres:16.4")
            .WithEnvironment("POSTGRES_PASSWORD", _password)
            .WithEnvironment("POSTGRES_USER", _username)
            .WithEnvironment("POSTGRES_DB", _database)
            .WithPortBinding(_port, true)
            .Build();
    }

    /// <summary>
    /// The raw, usable ADO.NET connection string for this container (real password included) --
    /// distinct from <see cref="IDatabaseContext.ConnectionString"/>, which returns a
    /// password-redacted string safe for logging. For tests that need to construct their own
    /// DatabaseContext (e.g. with a custom pool size) directly against this container.
    /// </summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("Container not started yet.");

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(_port);
        _connectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Password={_password};Database={_database};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=15;CommandTimeout=30;";
        await WaitForDbToStart(NpgsqlFactory.Instance, _connectionString, _container);
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