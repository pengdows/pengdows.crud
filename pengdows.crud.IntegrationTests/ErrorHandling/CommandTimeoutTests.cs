using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Live integration coverage for CommandTimeoutException. Deliberately self-contained (each test
/// spins up its own single standalone Docker container via a plain `docker run`, bypassing
/// Testcontainers/IntegrationTestFixture entirely) rather than using the shared 12-provider
/// fixture, which proved unreliable during this session's live-verification work (containers
/// intermittently wedged mid-startup with no bearing on this test's own logic). A single
/// standalone container per test is fast and has shown no flakiness.
/// </summary>
/// <remarks>
/// pengdows.crud never sets DbCommand.CommandTimeout itself — the only way to force a provider's
/// own client-side command-timeout exception is via that provider's connection-string-level
/// command-timeout key, paired with a query guaranteed to run longer than it:
///   - Npgsql (PostgreSql/CockroachDb/YugabyteDb): "CommandTimeout" (seconds) — confirmed live.
///   - Microsoft.Data.SqlClient (SqlServer): "Command Timeout" (seconds) — confirmed live.
///   - MySql.Data — Oracle's Connector/NET, confirmed via testbed.csproj's package reference,
///     NOT MySqlConnector (MySql/MariaDb/TiDb): NOT live-tested here. Confirmed via a standalone
///     probe that MySql.Data's async command execution (ExecuteNonQueryAsync) genuinely HANGS
///     against a real, healthy MySQL 9.6 server — it neither honors "Default Command Timeout"
///     nor completes even after the query's own real duration elapses (verified: the underlying
///     MySQL server itself was confirmed responsive via `docker exec ... mysql -e "SELECT 1"`
///     throughout). This is a known category of reliability problem with Oracle's official
///     connector's async support — the community-maintained MySqlConnector package exists
///     specifically to address it. Not something to work around here; MySql/MariaDb/TiDb's
///     CommandTimeoutException classification (error 1205, pre-existing) remains unit-tested only.
/// Only one representative per family is tested live (Postgres, SqlServer) since the other
/// Npgsql-family members (CockroachDb/YugabyteDb) share an identical driver code path.
/// Oracle (ODP.NET), Firebird, and Db2 have no equivalent connection-string-level command timeout
/// that reliably fires before the query itself completes; Sqlite/DuckDB are embedded engines with
/// no server-side "slow query over the wire" concept at all (Microsoft.Data.Sqlite's own timeout
/// setting governs lock-wait, not command execution). These are excluded entirely rather than
/// given a fabricated, untested trigger.
/// </remarks>
public class CommandTimeoutTests
{
    [Fact]
    public async Task Postgres_SlowQuery_ShortCommandTimeout_ThrowsCommandTimeoutException()
    {
        await using var container = await StandaloneContainer.StartAsync(
            "postgres:latest", 5432,
            new[] { "-e", "POSTGRES_PASSWORD=mysecretpassword" });

        var cs = $"Host=localhost;Port={container.HostPort};Username=postgres;Password=mysecretpassword;" +
                 "Database=postgres;CommandTimeout=1";

        await container.WaitForReadyAsync(Npgsql.NpgsqlFactory.Instance, cs);

        await using var conn = (DbConnection)Npgsql.NpgsqlFactory.Instance.CreateConnection()!;
        conn.ConnectionString = cs;
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_sleep(3)";

        var ex = await Record.ExceptionAsync(() => cmd.ExecuteNonQueryAsync());

        Assert.NotNull(ex);
        var translator = new pengdows.crud.exceptions.translators.PostgresExceptionTranslator();
        var translated = translator.Translate(
            pengdows.crud.enums.SupportedDatabase.PostgreSql, ex!, pengdows.crud.enums.DbOperationKind.Query);
        Assert.IsType<pengdows.crud.exceptions.CommandTimeoutException>(translated);
    }

    [Fact]
    public async Task SqlServer_SlowQuery_ShortCommandTimeout_ThrowsCommandTimeoutException()
    {
        await using var container = await StandaloneContainer.StartAsync(
            "mcr.microsoft.com/mssql/server:latest", 1433,
            new[] { "-e", "ACCEPT_EULA=Y", "-e", "MSSQL_SA_PASSWORD=yourStrong(!)Password" });

        var cs = $"Server=localhost,{container.HostPort};User Id=sa;Password=yourStrong(!)Password;" +
                 "TrustServerCertificate=true;Command Timeout=1;Connect Timeout=30";

        await container.WaitForReadyAsync(Microsoft.Data.SqlClient.SqlClientFactory.Instance, cs, 60);

        await using var conn = (DbConnection)Microsoft.Data.SqlClient.SqlClientFactory.Instance.CreateConnection()!;
        conn.ConnectionString = cs;
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "WAITFOR DELAY '00:00:03'";

        var ex = await Record.ExceptionAsync(() => cmd.ExecuteNonQueryAsync());

        Assert.NotNull(ex);
        var translator = new pengdows.crud.exceptions.translators.SqlServerExceptionTranslator();
        var translated = translator.Translate(
            pengdows.crud.enums.SupportedDatabase.SqlServer, ex!, pengdows.crud.enums.DbOperationKind.Query);
        Assert.IsType<pengdows.crud.exceptions.CommandTimeoutException>(translated);
    }

    private sealed class StandaloneContainer : IAsyncDisposable
    {
        public int HostPort { get; }
        private readonly string _name;

        private StandaloneContainer(string name, int hostPort)
        {
            _name = name;
            HostPort = hostPort;
        }

        public static async Task<StandaloneContainer> StartAsync(string image, int containerPort, string[] envArgs)
        {
            var name = $"pengdows_cmdtimeout_probe_{Guid.NewGuid():N}";
            var hostPort = GetFreeTcpPort();

            var args = new List<string> { "run", "-d", "--name", name };
            args.AddRange(envArgs);
            args.Add("-p");
            args.Add($"{hostPort}:{containerPort}");
            args.Add(image);

            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) startInfo.ArgumentList.Add(a);

            using var process = Process.Start(startInfo)!;
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new Xunit.SkipException(
                    $"Could not start standalone container {image}: {await process.StandardError.ReadToEndAsync()}");
            }

            return new StandaloneContainer(name, hostPort);
        }

        public async Task WaitForReadyAsync(DbProviderFactory factory, string connectionString, int timeoutSeconds = 30)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            Exception? lastError = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await using (var probe = (DbConnection)factory.CreateConnection()!)
                    {
                        probe.ConnectionString = connectionString;
                        await probe.OpenAsync();
                    }

                    // Some images (Postgres, MySQL) run a temporary init server that accepts
                    // connections briefly before restarting into the real server. Confirm
                    // stability with a second successful connect after a short delay before
                    // declaring readiness, so callers don't race the restart.
                    await Task.Delay(2000);
                    await using var probe2 = (DbConnection)factory.CreateConnection()!;
                    probe2.ConnectionString = connectionString;
                    await probe2.OpenAsync();
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await Task.Delay(1000);
                }
            }

            throw new Xunit.SkipException($"Container did not become ready in time: {lastError?.Message}");
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            using var stop = Process.Start("docker", $"stop {_name}");
            if (stop != null) await stop.WaitForExitAsync();
            using var rm = Process.Start("docker", $"rm {_name}");
            if (rm != null) await rm.WaitForExitAsync();
        }
    }
}
