#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
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
            services.GetRequiredService<ITypeMapRegistry>());
    }

    public async ValueTask DisposeAsync()
    {
          await _container.DisposeAsync();
    }
}