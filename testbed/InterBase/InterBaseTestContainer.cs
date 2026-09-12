using System.Diagnostics;
using InterBaseSql.Data.InterBaseClient;
using pengdows.crud;

namespace testbed.InterBase;

/// <summary>
/// Connects to an ALREADY-RUNNING, externally-managed InterBase 15 container instead of spinning
/// one up via Testcontainers — the only test container in this codebase shaped this way, for a
/// genuine reason confirmed while building it: InterBase's Developer Edition license binds to a
/// machine id derived from the container's IP address at one-time registration through a GUI
/// tool, and that registration state (plus every database file) lives in a persistent named
/// Docker volume, not the image. A fresh, anonymous, Testcontainers-managed container (even one
/// built from the same image, on the same static IP) would carry an empty volume with no license
/// registered — there is no way to complete that registration unattended, so this container
/// cannot be created fresh per run the way every other database in this testbed is. Opt-in via
/// <c>INCLUDE_INTERBASE=true</c> — see <see cref="ParallelTestOrchestrator"/>'s
/// <c>_includeInterBase</c> gate — for licensing reasons distinct from Snowflake's (cloud
/// credentials) and SAP HANA's (container RAM footprint).
/// <para>
/// Setup this container depends on, done once per machine (see the Dockerfile/docker-compose.yml
/// under the sibling <c>interbase/</c> project directory for the exact steps): build the
/// <c>interbase:15-dev</c> image from a licensed <c>InterBase_15_Linux.zip</c> installer (not
/// checked into this repo), run <c>docker compose --profile interbase up -d</c> once to create
/// the named container/volume/network on the fixed static IP this class connects to, then
/// complete the one-time GUI license registration against that same IP with the server stopped.
/// </para>
/// <para>
/// A second, host-side (not container-side) requirement makes this opt-in even on a machine that
/// already has the container running: <c>InterBaseSql.Data.InterBaseClient</c> P/Invokes a native
/// client library (<c>libgds.so</c>) that must be resolvable on whatever machine actually runs
/// this testbed .NET process — unlike every other database here, which either embeds its engine
/// or speaks a pure managed/TCP wire protocol needing no native library on the host at all. The
/// NuGet package does not bundle this library. A host without it installed (confirmed live on
/// this session's own research sandbox) fails with <c>DllNotFoundException</c> before ever
/// reaching the container.
/// </para>
/// </summary>
public class InterBaseTestContainer : TestContainer
{
    private const string _host = "172.28.0.10";
    private const int _port = 3050;
    private const string _database = "/opt/interbase/pengdows_test.ib";
    private const string _username = "sysdba";
    private const string _password = "masterkey";
    private const string _containerName = "interbase-interbase-1";
    private string? _connectionString;

    public override async Task StartAsync()
    {
        // Best-effort convenience for the common case (container exists, stopped) — mirrors what
        // an operator would otherwise run by hand. Not an error if this fails: the container may
        // already be running, or may not exist yet on this machine at all (opt-in, see class
        // remarks), in which case the connection retry loop below reports a clear timeout instead.
        TryStartExistingContainer();

        var csb = new IBConnectionStringBuilder
        {
            DataSource = _host,
            Port = _port,
            Database = _database,
            UserID = _username,
            Password = _password,
            Dialect = 3
        };
        _connectionString = csb.ConnectionString;

        await WaitForServerReadyAsync(TimeSpan.FromSeconds(120));
    }

    private static void TryStartExistingContainer()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", $"start {_containerName}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            process?.WaitForExit(10_000);
        }
        catch
        {
            // Docker CLI not on PATH, or the container doesn't exist on this machine — the
            // connection retry loop below is what actually decides whether this run can proceed.
        }
    }

    /// <summary>
    /// Custom retry loop rather than the shared <c>TestContainer.WaitForDbToStart</c> helper: that
    /// helper's signature requires a live Testcontainers <see cref="DotNet.Testcontainers.Containers.IContainer"/>
    /// instance (used only for a couple of provider-specific bootstrap branches, none of which
    /// apply here), which this container — deliberately not Testcontainers-managed, see class
    /// remarks — never has one of.
    /// </summary>
    private async Task WaitForServerReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var lastError = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new IBConnection(_connectionString);
                await connection.OpenAsync();
                await connection.CloseAsync();
                return;
            }
            catch (Exception ex)
            {
                if (ex.Message != lastError)
                {
                    Console.WriteLine($"  [waiting] InterBase not ready yet: {ex.Message}");
                }

                lastError = ex.Message;
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException(
            $"Could not connect to InterBase at {_host}:{_port} after {timeout.TotalSeconds}s. " +
            $"See {nameof(InterBaseTestContainer)}'s class remarks for the required one-time host setup " +
            "(licensed container image, docker-compose static IP, and a native libgds.so on this host).");
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(new DatabaseContext(_connectionString, InterBaseClientFactory.Instance));
    }

    /// <summary>
    /// The base connection string for this running container, exposed so callers can build their
    /// own <see cref="DatabaseContext"/> with additional connection-string parameters.
    /// </summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("Container not started yet.");

    protected override ValueTask DisposeAsyncCore()
    {
        // Deliberately does not stop/remove the container — it is a persistent, externally-managed
        // resource shared across runs (license/volume state must survive), not an ephemeral
        // Testcontainers-managed one. Same reasoning as why StartAsync only ever tries `docker
        // start`, never `docker run`/create.
        return ValueTask.CompletedTask;
    }
}
