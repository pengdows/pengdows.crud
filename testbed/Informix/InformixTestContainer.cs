using DotNet.Testcontainers.Builders;
using Informix.Net.Core;
using pengdows.crud;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

namespace testbed.Informix;

public class InformixTestContainer : TestContainer
{
    private const string _password = "in4mix";
    private const string _username = "informix";
    private const string _database = "testdb";
    private const string _serverName = "informixserver";
    private const int _port = 9088;
    private readonly IContainer _container;
    private string? _connectionString;
    private string? _sqlhostsPath;

    static InformixTestContainer()
    {
        // Idempotent — Program.cs already calls this before DbProviderFactoryFinder.FindAllFactories()
        // touches InformixClientFactory.Instance, but registering again here is harmless and
        // keeps this type safe to use standalone. Same pattern as Db2NativeLibraryBootstrap.
        InformixNativeLibraryBootstrap.Register();
    }

    public InformixTestContainer(string? image = null)
    {
        _container = new ContainerBuilder()
            .WithImage(image ?? "icr.io/informix/informix-developer-database:latest")
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("DBSERVERNAME", _serverName)
            .WithEnvironment("INFORMIX_PASSWORD", _password)
            .WithExposedPort(_port)
            .WithPortBinding(_port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(_port);

        // CONFIRMED live: the driver refuses to connect at all ("Sqlhosts file not found or
        // cannot be opened") without a real sqlhosts file — the package only ships
        // native/etc/sqlhosts.demo, a sample, not a file actually named "sqlhosts". The env var
        // itself is fixed and set early by InformixNativeLibraryBootstrap.Register() (setting
        // it here instead, only once the container's dynamic host port is known, was tried
        // first and confirmed NOT to work — same class of bug as LD_LIBRARY_PATH, the driver
        // appears to cache its environment at first native contact during factory discovery,
        // well before any container exists). Only this file's CONTENTS need to be current by
        // actual connection time, so it's safe to rewrite here. Format per sqlhosts.demo's own
        // header: "<dbservername> <nettype> <hostname> <servicename>"; onsoctcp = Informix
        // Dynamic Server, socket interface, TCP protocol.
        _sqlhostsPath = InformixNativeLibraryBootstrap.SqlHostsPath;
        File.WriteAllText(_sqlhostsPath, $"{_serverName}\tonsoctcp\tlocalhost\t{hostPort}\n");

        // UNVERIFIED live: this exact connection string was confirmed to PARSE correctly by
        // IfxConnectionStringBuilder (Host/Service/Server/Database/UID/PWD/Delimident are all
        // real, recognized keys — checked directly against the driver assembly, not assumed),
        // but has not yet been confirmed to actually CONNECT against a live server from this
        // session (no network path from this sandbox to a running container). Delimident=true
        // (not "y"/"1" — the builder only accepts standard .NET boolean text) is required for
        // ANSI double-quoted identifiers to be treated as identifiers rather than being
        // interchangeable with '...' string literals (see InformixDialect.cs's file-level
        // summary).
        _connectionString =
            $"Host=localhost;Service={hostPort};Server={_serverName};Database={_database};UID={_username};PWD={_password};Delimident=true;";

        await WaitForDbToStart(InformixClientFactory.Instance, _connectionString, _container, 180);
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(new DatabaseContext(_connectionString, InformixClientFactory.Instance));
    }

    /// <summary>
    /// The base connection string for this running container, exposed so callers can build
    /// their own <see cref="DatabaseContext"/> with additional connection-string parameters.
    /// </summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("Container not started yet.");

    protected override async ValueTask DisposeAsyncCore()
    {
        await _container.DisposeAsync();

        // Clear contents rather than delete: InformixNativeLibraryBootstrap.Register() expects
        // this fixed path to always exist for the lifetime of the process.
        if (_sqlhostsPath is not null)
        {
            File.WriteAllText(_sqlhostsPath, string.Empty);
        }
    }
}
