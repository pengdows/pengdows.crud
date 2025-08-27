#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySql.Data.MySqlClient;
using pengdows.crud;

#endregion

namespace testbed;

public class MariaDbContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    private string _database = "testdb";
    private string _password = "rootpassword";
    private int _port = 3306;
    private string _username = "root";

    // $ docker run --detach --name some-mariadb --env MARIADB_USER=example-user --env MARIADB_PASSWORD=my_cool_secret
    // --env MARIADB_DATABASE=exmple-database --env MARIADB_ROOT_PASSWORD=my-secret-pw  mariadb:latest
    // 

    public MariaDbContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("mariadb:latest")
            .WithEnvironment("MARIADB_ROOT_PASSWORD", _password)
            .WithEnvironment("MARIADB_DATABASE", _database)
            .WithEnvironment("MYSQL_SQL_MODE",
                "STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES")
            .WithPortBinding(_port, true)
            .WithExposedPort(_port)
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(_port);
        _connectionString =
            $@"Server=localhost;Port={hostPort};Database={_database};User={_username};Password={_password};";
        await WaitForDbToStart(MySqlClientFactory.Instance, _connectionString, _container);
    }

    public override async Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return new DatabaseContext(_connectionString, MySqlClientFactory.Instance,
            null);
    }

    public async ValueTask DisposeAsync()
    {
         await _container.DisposeAsync();
    }
}