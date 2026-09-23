using System.Text;
using DotNet.Testcontainers.Builders;
using pengdows.crud;
using Sap.Data.Hana;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

namespace testbed.SapHana;

/// <summary>
/// SAP HANA Express Edition (HXE) container. Opt-in only — see
/// <see cref="ParallelTestOrchestrator"/>'s <c>_includeSapHana</c> gate — because a working
/// container needs 16-32GB RAM per SAP's own guidance, far beyond a standard CI runner and every
/// other database in this testbed. Confirmed live on this session's own dev box (8 CPU/47GB RAM).
/// </summary>
public class HanaTestContainer : TestContainer
{
    private const string _masterPassword = "HXEHana1";
    private const string _username = "SYSTEM";
    private const string _database = "HXE";
    private const int _port = 39013;
    private readonly IContainer _container;
    private string? _connectionString;

    public HanaTestContainer(string? image = null)
    {
        // CONFIRMED live: the container needs a password.json under /hana/mounts (its own
        // --passwords-url argument points there) containing the master password, plus explicit
        // license acceptance. WithResourceMapping pushes the file directly into the container —
        // no host-side bind mount directory needed, so this works unmodified in CI.
        var passwordJson = Encoding.UTF8.GetBytes("{\"master_password\": \"" + _masterPassword + "\"}");

        _container = new ContainerBuilder()
            .WithImage(image ?? "saplabs/hanaexpress:latest")
            .WithExposedPort(_port)
            .WithPortBinding(_port, true)
            .WithResourceMapping(passwordJson, "/hana/mounts/password.json")
            .WithCommand("--passwords-url", "file:///hana/mounts/password.json", "--agree-to-sap-license")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_port))
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(_port);

        // CONFIRMED live: "Database=HXE" is required — without it, the driver routes to
        // SYSTEMDB (the multitenant system database) instead of the actual HXE tenant, and
        // HanaConnection.Open() fails with "error while parsing protocol: invalid action type".
        // Encrypt=false: this image has no TLS configured out of the box.
        _connectionString =
            $"Server=localhost:{hostPort};UserName={_username};Password={_masterPassword};Database={_database};Encrypt=false;";

        // CONFIRMED live: a cold first-time HXE startup (index server initialization, tenant
        // provisioning) took several minutes even with the image already pulled locally — far
        // longer than any other database in this testbed. No provider-specific "not ready yet"
        // exception type is caught here (unlike Oracle/Firebird/Sybase/Informix above); HanaException
        // during the startup window is handled fine by WaitForDbToStart's generic catch-and-retry.
        await WaitForDbToStart(HanaFactory.Instance, _connectionString, _container, 1200);
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(new DatabaseContext(_connectionString, HanaFactory.Instance));
    }

    /// <summary>
    /// The base connection string for this running container, exposed so callers can build their
    /// own <see cref="DatabaseContext"/> with additional connection-string parameters.
    /// </summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("Container not started yet.");

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
