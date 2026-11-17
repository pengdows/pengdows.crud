using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// G5 (CONCURRENCY.md), second provider. CommandCancellationStopsServerExecutionTests proved this
/// for PostgreSQL; this is the same claim against SQL Server, which has a different wire-level
/// cancellation mechanism (Microsoft.Data.SqlClient issues its own attention/cancel signal on the
/// TDS connection, not a separate out-of-band connection like Npgsql's CancelRequest) and a
/// different "is it still running" surface (<c>sys.dm_exec_requests</c>, not
/// <c>pg_stat_activity</c>). Same self-contained standalone-container pattern as
/// CommandTimeoutTests.cs/CommandCancellationStopsServerExecutionTests.cs, for the same reliability
/// reason (Testcontainers proved unreliable here independent of this test's own logic).
///
/// Lesson carried over from the PostgreSQL version: the first draft of that test appeared to
/// disprove G5 because its own verification query matched itself in the activity view. The
/// SQL Server query below excludes this session's own request the same way
/// (@@SPID is the current connection's own session id).
/// </summary>
[Collection(pengdows.crud.IntegrationTests.Infrastructure.StandaloneContainerCollection.Name)]
public class CommandCancellationStopsServerExecutionSqlServerTests
{
    [SkippableFact]
    public async Task SqlServer_CancellingARunningQuery_ActuallyStopsItOnTheServer()
    {
        Skip.IfNot(IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.SqlServer),
            "SQL Server is not enabled for this test run.");

        await using var container = await StandaloneContainer.StartAsync(
            "mcr.microsoft.com/mssql/server:latest", 1433,
            new[] { "-e", "ACCEPT_EULA=Y", "-e", "MSSQL_SA_PASSWORD=yourStrong(!)Password" });

        var cs = $"Server=localhost,{container.HostPort};User Id=sa;Password=yourStrong(!)Password;" +
                 "TrustServerCertificate=true;Connect Timeout=30";

        // SQL Server's first start intermittently exceeds 60s under full-suite load (same note as
        // CommandTimeoutTests.cs).
        await container.WaitForReadyAsync(SqlClientFactory.Instance, cs, 180);

        await using var ctx = new pengdows.crud.DatabaseContext(cs, SqlClientFactory.Instance);

        using var sc = ctx.CreateSqlContainer("WAITFOR DELAY '00:00:30'");
        using var cts = new CancellationTokenSource();

        var queryTask = sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text, cts.Token).AsTask();

        await Task.Delay(500);
        cts.Cancel();

        // CLAUDE.md: "OperationCanceledException is never wrapped — cancellation propagates
        // as-is." Same assertion as the PostgreSQL test, for the same reason.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queryTask);

        // sys.dm_exec_requests has one row per currently-executing request. Excluding this
        // probe's own session (@@SPID) is the SQL Server equivalent of the PostgreSQL test's
        // "pid != pg_backend_pid()" fix -- without it, this query would always find at least
        // itself and never report zero.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var stillRunning = true;
        while (DateTime.UtcNow < deadline)
        {
            await using var probe = (DbConnection)SqlClientFactory.Instance.CreateConnection()!;
            probe.ConnectionString = cs;
            await probe.OpenAsync();
            await using var check = probe.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM sys.dm_exec_requests " +
                "WHERE command = 'WAITFOR' AND session_id != @@SPID";
            var activeCount = (int)(await check.ExecuteScalarAsync())!;

            if (activeCount == 0)
            {
                stillRunning = false;
                break;
            }

            await Task.Delay(250);
        }

        Assert.False(stillRunning,
            "WAITFOR DELAY '00:00:30' was still running in sys.dm_exec_requests well after the " +
            "client-side cancellation completed -- the token reached the .NET call but not the server.");
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
            var name = $"pengdows_cancel_sqlserver_probe_{Guid.NewGuid():N}";
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
            foreach (var a in args)
            {
                startInfo.ArgumentList.Add(a);
            }

            using var process = Process.Start(startInfo)!;
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
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

            throw new InvalidOperationException($"Container did not become ready in time: {lastError?.Message}");
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
            if (stop != null)
            {
                await stop.WaitForExitAsync();
            }
            using var rm = Process.Start("docker", $"rm {_name}");
            if (rm != null)
            {
                await rm.WaitForExitAsync();
            }
        }
    }
}
