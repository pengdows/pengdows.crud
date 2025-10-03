using System.Data.Common;
using DotNet.Testcontainers.Builders;
using pengdows.crud;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

namespace testbed;

public class Db2TestContainer : TestContainer
{
    private const string _password = "MyStr0ngP@ssw0rd";
    private const string _username = "db2inst1";
    private const string _database = "testdb";
    private const int _port = 50000;
    private readonly IContainer _container;
    private string? _connectionString;

    public Db2TestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("ibmcom/db2:latest")
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("DB2INST1_PASSWORD", _password)
            .WithEnvironment("DBNAME", _database)
            .WithExposedPort(_port)
            .WithPortBinding(_port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(_port);

        _connectionString = $"Server=localhost:{hostPort};Database={_database};UID={_username};PWD={_password};";

        await Task.Delay(30000);
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        throw new NotSupportedException("DB2 provider not configured - add IBM.Data.DB2 package and factory");
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
