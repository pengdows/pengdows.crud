using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Data;
using System.Data.Common;
using Npgsql;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// G5 (CONCURRENCY.md): "Cancelling a running command stops it on the server." This is the one
/// guarantee that genuinely cannot be tested with fakeDb -- a mock reporting "yes, cancellation
/// reached the server and it stopped" would just be the mock agreeing with itself; the claim is
/// specifically about whether pengdows.crud's own CancellationToken plumbing (through
/// SqlContainer -> the real ADO.NET command -> the wire protocol) actually reaches a real server
/// process, not just that the .NET call returns.
///
/// Deliberately self-contained (its own standalone Docker container via a plain `docker run`,
/// bypassing Testcontainers/IntegrationTestFixture), the same pattern CommandTimeoutTests.cs uses
/// and for the same reason: the shared 12-provider fixture proved unreliable during live
/// verification work, independent of this test's own logic.
///
/// Unlike CommandTimeoutTests (which intentionally bypasses pengdows.crud to test the exception
/// translator directly against a raw ADO.NET command), this test goes through a real
/// DatabaseContext/SqlContainer, because the claim under test is specifically about
/// pengdows.crud's own cancellation propagation, not Npgsql's.
/// </summary>
[Collection(pengdows.crud.IntegrationTests.Infrastructure.StandaloneContainerCollection.Name)]
public class CommandCancellationStopsServerExecutionTests
{
    [SkippableFact]
    public async Task Postgres_CancellingARunningQuery_ActuallyStopsItOnTheServer()
    {
        Skip.IfNot(IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.PostgreSql),
            "PostgreSQL is not enabled for this test run.");

        await using var container = await StandaloneContainer.StartAsync(
            "postgres:latest", 5432,
            new[] { "-e", "POSTGRES_PASSWORD=mysecretpassword" });

        var cs = $"Host=localhost;Port={container.HostPort};Username=postgres;Password=mysecretpassword;" +
                 "Database=postgres";

        await container.WaitForReadyAsync(NpgsqlFactory.Instance, cs);

        await using var ctx = new pengdows.crud.DatabaseContext(cs, NpgsqlFactory.Instance);

        // A query long enough that it is certainly still running when we cancel it a few hundred
        // milliseconds in, and long enough that "it happened to finish on its own before the
        // pg_stat_activity check" cannot explain a false pass.
        using var sc = ctx.CreateSqlContainer("SELECT pg_sleep(30)");
        using var cts = new CancellationTokenSource();

        var queryTask = sc.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, cts.Token).AsTask();

        await Task.Delay(500);
        cts.Cancel();

        // CLAUDE.md: "OperationCanceledException is never wrapped — cancellation propagates
        // as-is." If pengdows.crud ever started swallowing or translating it into something else,
        // this is the assertion that would catch it.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queryTask);

        // The real claim: not just that the .NET call returned, but that the query is no longer
        // running on the server. Poll pg_stat_activity on a fresh connection for a few seconds --
        // Npgsql's cancellation is a real wire-protocol CancelRequest sent over a second
        // connection, processed asynchronously by the server, so this is not necessarily
        // instantaneous.
        //
        // "query ILIKE '%pg_sleep%'" must exclude this probe's own backend
        // (pid != pg_backend_pid()): this very statement's text also contains the substring
        // "pg_sleep", so an unfiltered count always finds at least itself and never reaches zero.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var stillRunning = true;
        while (DateTime.UtcNow < deadline)
        {
            await using var probe = (DbConnection)NpgsqlFactory.Instance.CreateConnection()!;
            probe.ConnectionString = cs;
            await probe.OpenAsync();
            await using var check = probe.CreateCommand();
            check.CommandText =
                "SELECT count(*) FROM pg_stat_activity " +
                "WHERE state = 'active' AND query ILIKE '%pg_sleep%' AND pid != pg_backend_pid()";
            var activeCount = (long)(await check.ExecuteScalarAsync())!;

            if (activeCount == 0)
            {
                stillRunning = false;
                break;
            }

            await Task.Delay(250);
        }

        Assert.False(stillRunning,
            "pg_sleep(30) was still active in pg_stat_activity well after the client-side " +
            "cancellation completed -- the token reached the .NET call but not the server.");
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
            var name = $"pengdows_cancel_probe_{Guid.NewGuid():N}";
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
