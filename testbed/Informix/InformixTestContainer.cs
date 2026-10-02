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
    private const int DeveloperImagePoolSize = 5;
    private readonly IContainer _container;
    private string? _connectionString;
    private string? _sqlhostsPath;

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
        // Deliberately NOT in a static constructor (moved from one — see git history): a static
        // cctor fires the instant this type is even CONSTRUCTED, which happens merely building a
        // TestConfiguration list for inspection (ParallelTestOrchestrator.GetTestConfigurations,
        // called from unit tests that never intend to actually run Informix) — eagerly triggering
        // native-library re-exec/registration with no real driver use ever coming. Registering
        // here instead, immediately before the first real native contact, keeps
        // ParallelTestOrchestrator's list-building side-effect-free while still satisfying
        // InformixNativeLibraryBootstrap's "before ANY access to InformixClientFactory" contract
        // for actual runs — Register() is idempotent, so this is a no-op when Program.cs's own
        // earlier, explicit call (testbed's real entry point) already ran it.
        InformixNativeLibraryBootstrap.Register();

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

        // The test database is created with a UTF-8 locale (DB_LOCALE=en_US.utf8 at CREATE DATABASE)
        // and the client uses the same, so non-ASCII text round-trips; the image's default locale
        // (en_US.819) rejects it ("Inexact character conversion during translation").
        // CONFIRMED live: this exact connection string parses correctly and connects
        // (Host/Service/Server/Database/UID/PWD/Delimident are all real, recognized keys).
        // Delimident=true (not "y"/"1" — the builder only accepts standard .NET boolean text) is
        // required for ANSI double-quoted identifiers to be treated as identifiers rather than
        // being interchangeable with '...' string literals (see InformixDialect.cs's file-level
        // summary).
        _connectionString =
            $"Host=localhost;Service={hostPort};Server={_serverName};Database={_database};UID={_username};PWD={_password};Delimident=true;DB_LOCALE=en_US.utf8;CLIENT_LOCALE=en_US.utf8;";

        // CONFIRMED live: the informix-developer-database image never creates a user database on
        // its own — its own informix_setup_user_db.sh entrypoint step only runs a schema script
        // when a DB_SCHEMA env var points to one, which this container doesn't set. Connecting
        // straight to Database={_database} therefore fails forever with "Database not found or
        // no system permission", not a transient not-ready-yet error — sysmaster is the one
        // database guaranteed to exist the moment the engine reaches On-Line mode, so wait on
        // that first, then create the real test database once, before switching to it.
        var systemConnectionString =
            $"Host=localhost;Service={hostPort};Server={_serverName};Database=sysmaster;UID={_username};PWD={_password};Delimident=true;";
        await WaitForDbToStart(InformixClientFactory.Instance, systemConnectionString, _container, 180);

        // CONFIRMED live: a .NET driver connection to sysmaster kept hitting "Database is
        // currently opened by another user" indefinitely (not just transiently) even well after
        // WaitForDbToStart's own connect+close against it succeeded once - some internal
        // engine/pooling behavior of the ODBC-backed driver against sysmaster specifically, not
        // reproduced when going through dbaccess (the engine's own bundled CLI) instead, which is
        // what informix_setup_user_db.sh itself uses. Sidesteps the .NET driver connection
        // entirely for this one-time step - only the real per-test connections in
        // GetDatabaseContextAsync go through the driver. Docker exec already runs as root inside
        // this container (confirmed live - no "su"/password needed), so this only needs the
        // Informix environment (INFORMIXDIR/PATH/etc.) sourced from the image's own env script,
        // not an OS user switch.
        var createDeadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var result = await _container.ExecAsync(new[]
            {
                "bash", "-c",
                $"source /opt/ibm/scripts/informix_inf.env && " +
                $"export DB_LOCALE=en_US.utf8 && dbaccess sysmaster - <<< 'CREATE DATABASE {_database} WITH LOG;'"
            });

            if (result.ExitCode == 0)
            {
                break;
            }

            if (DateTime.UtcNow >= createDeadline)
            {
                throw new InvalidOperationException(
                    $"Informix CREATE DATABASE {_database} failed: {result.Stdout} {result.Stderr}");
            }

            await Task.Delay(1000);
        }

        // CONFIRMED live: the image names a default smart-blob space (SBSPACENAME sbspace) but never
        // creates it, so every BLOB/CLOB write failed with "Invalid default sbspace name (sbspace)".
        var sbspace = await _container.ExecAsync(new[]
        {
            "bash", "-c",
            "source /opt/ibm/scripts/informix_inf.env && " +
            "touch /opt/ibm/data/spaces/sbspace.000 && chmod 660 /opt/ibm/data/spaces/sbspace.000 && " +
            "onspaces -c -S sbspace -p /opt/ibm/data/spaces/sbspace.000 -o 0 -s 100000 -Df 'LOGGING=ON'"
        });
        if (sbspace.ExitCode != 0)
        {
            throw new InvalidOperationException($"Informix sbspace creation failed: {sbspace.Stdout} {sbspace.Stderr}");
        }
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        // CONFIRMED live: the developer image accepts about 24 concurrent sessions; the 25th fails
        // with -25571 "Cannot create a user thread". Size each pool (reader and writer) so a context,
        // plus a second one some tests open, stays below that; the governor queues the rest.
        return Task.FromResult<IDatabaseContext>(new DatabaseContext(
            new pengdows.crud.configuration.DatabaseContextConfiguration
            {
                ConnectionString = _connectionString,
                MaxConcurrentReads = DeveloperImagePoolSize,
                MaxConcurrentWrites = DeveloperImagePoolSize
            },
            InformixClientFactory.Instance));
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
