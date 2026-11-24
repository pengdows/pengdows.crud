using DotNet.Testcontainers.Builders;
using IBM.Data.Db2;
using pengdows.crud;
using IContainer = DotNet.Testcontainers.Containers.IContainer;

namespace testbed.Db2;

public class Db2TestContainer : TestContainer
{
    private const string _password = "MyStr0ngP@ssw0rd";
    private const string _username = "db2inst1";
    private const string _securityAdministrator = "lbacadm";
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
            .WithImage(image ?? TestbedImageMatrix.Latest("Db2"))
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
        // The LBAC fixture (TypeFidelityIntegrationTests) grants labels as this security administrator: the instance owner cannot.
        await CreateOsUserAsync(_securityAdministrator, _password);

        var hostPort = _container.GetMappedPublicPort(_port);

        _connectionString = $"Server=localhost:{hostPort};Database={_database};UID={_username};PWD={_password};";

        // The readiness check runs without pooling, so its physical connection really closes: a pooled one can outlive Close()
        // and keep the database in use while the activation below runs.
        await WaitForDbToStart(DB2Factory.Instance, _connectionString + "Pooling=false;", _container, 300);

        // Explicit activation, from the instance owner's command line: an activated database stays up after its last
        // connection closes, so no test connection finds it deactivated. The readiness probe above closes its connection
        // just before this runs, and Db2 can still be deactivating then: the activation is refused (SQL1035N) until the
        // deactivation finishes (observed 2026-11-22). Retry only that refusal, for a bounded time.
        const int activationAttempts = 15;
        for (var attempt = 1; ; attempt++)
        {
            var activation = await _container.ExecAsync(new[]
            {
                "/bin/sh", "-c", "su - db2inst1 -c '/opt/ibm/db2/V11.5/bin/db2 activate database testdb'"
            });
            // SQL1490W: the database was already active, so the activation is in place; the CLP reports that as exit code 2.
            if (activation.ExitCode == 0 || (activation.Stdout + activation.Stderr).Contains("SQL1490W", StringComparison.Ordinal))
            {
                return;
            }

            var refused = (activation.Stdout + activation.Stderr).Contains("SQL1035N", StringComparison.Ordinal);
            if (!refused || attempt == activationAttempts)
            {
                var state = await CaptureActivationStateAsync();
                throw new InvalidOperationException(
                    $"Activating {_database} failed on attempt {attempt} ({activation.ExitCode}): {activation.Stderr}{activation.Stdout}" +
                    $"\nServer state at refusal:\n{state}");
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// The server's view of the database at the moment activation is refused: its active state, the applications connected
    /// to it, and the end of the diagnostic log. Run inside the container as the instance owner.
    /// </summary>
    private async Task<string> CaptureActivationStateAsync()
    {
        var report = new System.Text.StringBuilder();
        foreach (var (title, command) in new[]
                 {
                     ("list active databases", new[] { "su", "-", "db2inst1", "-c", "/opt/ibm/db2/V11.5/bin/db2 list active databases" }),
                     ("list applications", new[] { "su", "-", "db2inst1", "-c", "/opt/ibm/db2/V11.5/bin/db2 list applications for database testdb show detail" }),
                     ("db2diag tail", new[] { "/bin/sh", "-c", "tail -n 200 /database/config/db2inst1/sqllib/db2dump/DIAG0000/db2diag.log" }),
                 })
        {
            var result = await _container.ExecAsync(command);
            report.AppendLine($"--- {title} (exit {result.ExitCode})").Append(result.Stdout).Append(result.Stderr);
        }

        report.AppendLine("--- container log (tail)").Append(await ContainerLogTailAsync());
        return report.ToString();
    }

    /// <summary>The last lines of the container's own log (its start-up and setup progress), read with the Docker CLI.</summary>
    private async Task<string> ContainerLogTailAsync()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("docker", new[] { "logs", "--tail", "300", _container.Id })
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            return "(docker CLI not available)";
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout + stderr;
    }

    /// <summary>
    /// Creates an operating-system user inside the container (as root), so Db2 can authenticate it. Db2 LBAC needs a
    /// security administrator other than the instance owner, because the instance owner cannot grant SECADM or a label to
    /// itself (SQL0554N). Returns the command's exit code.
    /// </summary>
    public async Task<long> CreateOsUserAsync(string user, string password)
    {
        var script = $"id -u {user} >/dev/null 2>&1 || useradd -m {user}; echo '{user}:{password}' | chpasswd";
        var result = await _container.ExecAsync(new[] { "/bin/sh", "-c", script });
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating OS user '{user}' failed ({result.ExitCode}): {result.Stderr}{result.Stdout}");
        }

        return result.ExitCode;
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

    private const string _probeDatabase = "probedb";
    private string? _probeConnectionString;

    /// <summary>
    /// Creates (once) a second database that nothing else in the run connects to, and returns an
    /// unpooled connection string for it. The idle-unload probe needs Db2 to really deactivate a
    /// database between samples; the shared testdb never deactivates while other checks' pooled
    /// connections are open, and the only way to drop those (<c>DB2Connection.ReleaseObjectPool</c>)
    /// crashes the IBM driver under later concurrent use. Db2 has no SQL CREATE DATABASE, so this
    /// runs the CLP inside the container.
    /// </summary>
    public async Task<string> EnsureIdleProbeDatabaseAsync()
    {
        if (_probeConnectionString is not null)
        {
            return _probeConnectionString;
        }

        var result = await _container.ExecAsync(new[]
        {
            "su", "-", _username, "-c",
            $"db2 list db directory | grep -qi 'alias.*= {_probeDatabase}$' || db2 create database {_probeDatabase}"
        });
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not create Db2 probe database '{_probeDatabase}' (exit {result.ExitCode}): {result.Stdout} {result.Stderr}");
        }

        var hostPort = _container.GetMappedPublicPort(_port);
        _probeConnectionString =
            $"Server=localhost:{hostPort};Database={_probeDatabase};UID={_username};PWD={_password};";
        return _probeConnectionString;
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
