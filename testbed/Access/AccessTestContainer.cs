using System.Data.OleDb;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace testbed.Access;

/// <summary>
/// Microsoft Access (Jet/ACE) has no Docker image at all — a fourth, distinct opt-in
/// justification alongside Snowflake (cloud credentials), SAP HANA (16-32GB RAM), and InterBase
/// (node-locked license + native library). Runs whenever the OS is Windows — see
/// <see cref="ParallelTestOrchestrator"/>'s <c>_includeAccess</c> gate.
/// <para>
/// Modeled on <see cref="SqliteTestContainer"/> (file-based, no Testcontainers/Docker at all —
/// this is the right template, not <c>InterBaseTestContainer</c>'s externally-managed-container
/// pattern, since Access needs no persistent external server). One necessary difference from
/// SQLite: ACE does not auto-create the <c>.accdb</c> file on connect, so <see cref="StartAsync"/>
/// creates it first via ADOX COM interop — proven live this session. Windows-only (ADOX COM
/// interop and the ACE OLE DB provider both require it), guarded with
/// <see cref="OperatingSystem.IsWindows"/>.
/// </para>
/// </summary>
public class AccessTestContainer : TestContainer
{
    private string? _connectionString;
    private string? _dbFilePath;

    public override Task StartAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Microsoft Access (Jet/ACE) requires Windows — the ACE OLE DB provider and ADOX COM interop used to create the .accdb file are both Windows-only.");
        }

        _dbFilePath = Path.Combine(Path.GetTempPath(), $"pengdows.integration.{Guid.NewGuid():N}.accdb");
        _connectionString = $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={_dbFilePath};";

        // ACE does not auto-create the file on connect (unlike SQLite) — create it explicitly via
        // ADOX.Catalog, the proven mechanism for this (confirmed live this session).
        dynamic catalog = Activator.CreateInstance(
            Type.GetTypeFromProgID("ADOX.Catalog")
                ?? throw new InvalidOperationException(
                    "ADOX.Catalog COM type not found — the Microsoft Access Database Engine Redistributable does not appear to be installed on this host."))!;
        try
        {
            catalog.Create(_connectionString);

            // Create leaves the new file open through the catalog's ActiveConnection. Close it and release both COM
            // objects now. Measured over repeated testbed runs: left to the finalizer, every run leaked its .accdb
            // (DisposeAsyncCore's delete failed on the open file); with the release no file leaked in 460 runs.
            // This does NOT stop the separate intermittent hang of the worker process at exit: that still happened
            // about a third of the time (testbed.exe, as `dotnet run` starts it) with this release in place.
            dynamic connection = catalog.ActiveConnection;
            try
            {
                connection.Close();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(connection);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(catalog);
        }

        return Task.CompletedTask;
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString == null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = _connectionString,
            DbMode = DbMode.Best
        };
        var context = new DatabaseContext(
            config,
            OleDbFactory.Instance,
            null,
            new TypeMapRegistry());
        return Task.FromResult<IDatabaseContext>(context);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        if (!string.IsNullOrWhiteSpace(_dbFilePath))
        {
            try
            {
                if (File.Exists(_dbFilePath))
                {
                    File.Delete(_dbFilePath);
                }
            }
            catch
            {
                // best-effort cleanup; ignore failures
            }
        }

        return ValueTask.CompletedTask;
    }
}
