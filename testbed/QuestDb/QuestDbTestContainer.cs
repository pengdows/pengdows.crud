#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud;

#endregion

namespace testbed.QuestDb;

public class QuestDbTestContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    private const string _database = "qdb";
    private const int _port = 8812;
    private const string _username = "admin";
    private const string _password = "quest";

    public QuestDbTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("questdb/questdb:latest")
            .WithPortBinding(_port, true)
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(_port);
        _connectionString =
            $@"Host=localhost;Port={hostPort};Username={_username};Password={_password};Database={_database};Pooling=false;Timeout=15;CommandTimeout=30;";
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
