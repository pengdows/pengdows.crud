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
            .WithPortBinding(_port, true) // dynamic host binding
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(_port);
        
        // DB2 connection string format
        _connectionString = $"Server=localhost:{hostPort};Database={_database};UID={_username};PWD={_password};";

        // Wait until the DB is accepting connections
        // Note: DB2 factory would need to be available for this to work
        // await WaitForDbToStart(Db2Factory.Instance, _connectionString, _container, 300);
        
        // For now, just wait a fixed time for DB2 to be ready
        await Task.Delay(30000); // 30 seconds
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        // Note: This would require IBM.Data.DB2 package
        // return Task.FromResult<IDatabaseContext>(
        //     new DatabaseContext(_connectionString, DB2Factory.Instance, null!));
        
        throw new NotSupportedException("DB2 provider not configured - add IBM.Data.DB2 package and factory");
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}