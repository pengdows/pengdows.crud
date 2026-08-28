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
    private string _database = "testdb";
    private string _password = "rootpassword";
    private int _port = 3306;
    private string _username = "root";

    public MariaDbContainer(string? image = null)
    {
        _container = new ContainerBuilder()
            .WithImage(image ?? "mariadb:11.4.12")
            .WithEnvironment("MARIADB_ROOT_PASSWORD", _password)
            .WithEnvironment("MYSQL_ROOT_PASSWORD", _password)
            .WithEnvironment("MARIADB_DATABASE", _database)
            .WithEnvironment("MYSQL_DATABASE", _database)
            .WithEnvironment("MYSQL_SQL_MODE",
                "STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES")
            .WithCommand("--character-set-server=utf8mb4", "--collation-server=utf8mb4_unicode_ci")
            .WithPortBinding(_port, true)
            .WithExposedPort(_port)
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(_port);
        _connectionString =
            $@"Server=localhost;Port={hostPort};Database={_database};User ID={_username};Password={_password};";
        await WaitForDbToStart(MySqlConnectorFactory.Instance, _connectionString, _container);
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

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}