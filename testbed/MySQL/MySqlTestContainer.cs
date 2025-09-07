#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySql.Data.MySqlClient;
using pengdows.crud;

#endregion

namespace testbed;

public class MySqlTestContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    private string _database = "testdb";
    private string _password = "rootpassword";
    private int _port = 3306;
    private string _username = "root";

    // run --name mysql-container -e MYSQL_ROOT_PASSWORD=rootpassword -e MYSQL_DATABASE=testdb -p 3306:3306 -d mysql:latest

    public MySqlTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("mysql:latest")
            .WithEnvironment("MYSQL_ROOT_PASSWORD", _password)
            .WithEnvironment("MYSQL_DATABASE", _database)
            .WithEnvironment("MYSQL_SQL_MODE",
                "ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_ZERO_DATE,NO_ZERO_IN_DATE,NO_ENGINE_SUBSTITUTION")
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

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, MySqlClientFactory.Instance, null!));
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
