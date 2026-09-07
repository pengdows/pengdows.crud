using DotNet.Testcontainers.Builders;
using IBM.Data.Db2;
using pengdows.crud;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

namespace testbed.Db2;

public class Db2TestContainer : TestContainer
{
    private const string _password = "MyStr0ngP@ssw0rd";
    private const string _username = "db2inst1";
    private const string _database = "testdb";
    private const int _port = 50000;
    private readonly IContainer _container;
    private string? _connectionString;

    static Db2TestContainer()
    {
        // Idempotent — Program.cs already calls this before DbProviderFactoryFinder.FindAllFactories()
        // touches DB2Factory.Instance, but registering again here is harmless and keeps this type
        // safe to use standalone (e.g. from pengdows.crud.IntegrationTests via CreateContainerAsync).
        Db2NativeLibraryBootstrap.Register();
    }

    public Db2TestContainer(string? image = null)
    {
        _container = new ContainerBuilder()
            .WithImage(image ?? "ibmcom/db2:11.5.8.0")
            .WithPrivileged(true)
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("DB2INST1_PASSWORD", _password)
            .WithEnvironment("DBNAME", _database)
            // Default (true) configures archive logging (LOGARCHMETH1=DISK:...), which requires
            // a full backup before the database will accept sustained connections. Under this
            // suite's workload (many test classes repeatedly creating/dropping tables against one
            // shared container) that fills the log fast enough to push the database into
            // "backup pending" mid-run (SQL1116N/SQL1035N), failing every subsequent test. Circular
            // logging has no such requirement and is the right choice for an ephemeral test database.
            .WithEnvironment("ARCHIVE_LOGS", "false")
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

        await WaitForDbToStart(DB2Factory.Instance, _connectionString, _container, 300);
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(new DatabaseContext(_connectionString, DB2Factory.Instance));
    }

    /// <summary>
    /// The base connection string for this running container, exposed so callers can build their
    /// own <see cref="DatabaseContext"/> with additional connection-string parameters (e.g. pool
    /// sizing for tests that need to force physical-connection reuse).
    /// </summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("Container not started yet.");

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
