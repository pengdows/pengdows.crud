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
    private const string _database = "test";
    private const int _port = 4000;
    private const string _username = "root";

    public TiDBTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("pingcap/tidb:latest")
            .WithPortBinding(_port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(_port);
        // TiDB default has no password for root
        _connectionString =
            $@"Server=localhost;Port={hostPort};User={_username};Database={_database};Pooling=true;MinimumPoolSize=1;MaximumPoolSize=100;ConnectionTimeout=15;";
        await WaitForDbToStart(MySqlClientFactory.Instance, _connectionString, _container);
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

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}