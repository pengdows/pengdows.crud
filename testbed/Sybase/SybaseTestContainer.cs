#region

using System.Text;
using AdoNetCore.AseClient;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using pengdows.crud;

#endregion

namespace testbed.Sybase;

/// <summary>
/// Runs SAP ASE 16 Developer Edition via the community <c>nguoianphu/docker-sybase</c> image.
/// </summary>
/// <remarks>
/// This image ships an ASE binary (compiled 2015) that reliably crashes with
/// "Current process infected with signal 11 (SIGSEGV)" in <c>Snap::Validate()</c> on modern
/// host kernels (SAP KBA 3018138 — a documented ASE issue, not specific to this image). The
/// fix is trace flag <c>-T11889</c>, which disables SNAP struct validation at boot. Since the
/// flag can only be applied by editing the server's startup script, this container starts once
/// (the crash leaves the outer container process running — only the inner <c>dataserver</c>
/// process dies), patches the script in place, and restarts the container so ASE reboots with
/// the flag applied.
/// The image also does not honor an <c>SA_PASSWORD</c> environment variable — its entrypoint
/// (<c>/sybase-entrypoint.sh</c>) never reads it — so the real credentials are whatever is baked
/// into the image (<c>sa</c> / <c>myPassword</c>).
/// </remarks>
public class SybaseTestContainer : TestContainer, ITestContainer
{
    private const string Username = "sa";
    private const string Password = "myPassword";
    private const string Database = "testdb";
    private const string RunScriptPath = "/opt/sybase/ASE-16_0/install/RUN_MYSYBASE";
    private readonly IContainer _container;
    private string? _connectionString;

    public SybaseTestContainer()
    {
        _container = new ContainerBuilder()
            .WithImage("nguoianphu/docker-sybase")
            .WithPortBinding(5000, true)
            .WithPortBinding(5001, true)
            .WithWaitStrategy(Wait.ForUnixContainer())
            .Build();
    }

    public override async Task StartAsync()
    {
        await _container.StartAsync();
        await ApplySigsegvWorkaroundAsync();

        var hostPort = _container.GetMappedPublicPort(5000);
        var cs = $"DataSource=localhost;Port={hostPort};Database=master;Uid={Username};Pwd={Password};";
        await WaitForDbToStart(AseClientFactory.Instance, cs, _container);

        await CreateTestDatabase(cs);

        _connectionString = $"DataSource=localhost;Port={hostPort};Database={Database};Uid={Username};Pwd={Password};";
    }

    /// <summary>
    /// Appends the <c>-T11889</c> trace flag to the ASE startup script and restarts the
    /// container so the server boots with SNAP struct validation disabled (SAP KBA 3018138).
    /// The script's last argument line already ends with a line-continuing backslash, so this
    /// is a pure append, not a rewrite.
    /// </summary>
    private async Task ApplySigsegvWorkaroundAsync()
    {
        var current = await _container.ReadFileAsync(RunScriptPath);
        var currentText = Encoding.UTF8.GetString(current);
        if (currentText.Contains("-T11889"))
        {
            return;
        }

        var patched = Encoding.UTF8.GetBytes(currentText.TrimEnd('\n') + "\n-T11889\n");
        await _container.CopyAsync(patched, RunScriptPath, UnixFileModes.UserRead | UnixFileModes.UserWrite |
                                                             UnixFileModes.UserExecute | UnixFileModes.GroupRead |
                                                             UnixFileModes.GroupExecute | UnixFileModes.OtherRead |
                                                             UnixFileModes.OtherExecute);

        await _container.StopAsync();
        await _container.StartAsync();
    }

    /// <summary>
    /// Creates the test database on a dedicated data device.
    /// </summary>
    /// <remarks>
    /// The image's default <c>master</c> device (384 MB) is almost entirely consumed by ASE's
    /// own system catalogs and only has ~12 MB free — not enough for even a default-sized
    /// <c>CREATE DATABASE</c> (verified live: "must be at least 24 megabytes so Model Database
    /// can be copied"). A dedicated device with its own backing file gives the test database
    /// room independent of how full the system devices are.
    /// </remarks>
    private static async Task CreateTestDatabase(string masterCs)
    {
        await using var conn = AseClientFactory.Instance.CreateConnection();
        conn.ConnectionString = masterCs;
        await conn.OpenAsync();

        async Task Exec(string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = $"SELECT name FROM sysdatabases WHERE name = '{Database}'";
            var exists = await checkCmd.ExecuteScalarAsync() != null;
            if (exists)
            {
                return;
            }
        }

        await Exec("DISK INIT NAME='testdata', PHYSNAME='/opt/sybase/data/testdata.dat', SIZE='60M'");
        await Exec($"CREATE DATABASE {Database} ON testdata = '50M'");

        // ASE defaults new columns to NOT NULL when a CREATE TABLE statement omits an explicit
        // NULL/NOT NULL keyword (the opposite default from every other dialect this testbed
        // exercises). testbed/TestProvider.cs relies on the ANSI-standard "nullable unless
        // stated otherwise" default for several columns, so flip that default for this database.
        // sp_dboption's change only takes effect after a checkpoint issued while connected to
        // that database (Sybase convention), hence the second connection below.
        await Exec($"EXEC sp_dboption {Database}, 'allow nulls by default', true");

        await using var dbConn = AseClientFactory.Instance.CreateConnection();
        dbConn.ConnectionString = masterCs.Replace("Database=master", $"Database={Database}");
        await dbConn.OpenAsync();
        await using var checkpointCmd = dbConn.CreateCommand();
        checkpointCmd.CommandText = "CHECKPOINT";
        await checkpointCmd.ExecuteNonQueryAsync();
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(
                _connectionString,
                AseClientFactory.Instance,
                null!));
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }
}
